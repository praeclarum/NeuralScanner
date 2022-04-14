namespace NeuralScanner

open System
open System.Buffers
open System.Collections.Concurrent
open System.IO
open System.Numerics
open System.Globalization
open System.Threading.Tasks

open SceneKit
open MetalTensors
open SdfKit

type SdfDataSet (project : Project, samplingDistance : float32, outputScale : float32) =
    inherit DataSet ()

    let dataDirectory = project.CaptureDirectory

    let depthFiles = project.DepthPaths
    let count = depthFiles |> Seq.sumBy (fun x -> 1)
    do if count = 0 then failwithf "No files in %s" dataDirectory

    let frames = project.GetVisibleFrames ()

    // Clipping information
    let meanCenter = project.Settings.ClipTranslation
    let volumeMin, volumeMax =
        let vmin = meanCenter - project.Settings.ClipScale
        let vmax = meanCenter + project.Settings.ClipScale
        vmin, vmax        
    let volumeCenter = meanCenter

    let occupancy = AxisOccupancy.Create 64
    let clipTransform, inverseClipTransform =
        let clipToWorld = SceneKitGeometry.matrixFromSCNMatrix4 project.ClipTransform
        let mutable worldToClip = clipToWorld
        if Matrix4x4.Invert (clipToWorld, &worldToClip) then
            for f in frames do
                f.SetClip (worldToClip, clipToWorld, occupancy)
        clipToWorld, worldToClip
    let unoccupied = occupancy.GetUnoccupied ()

    let createBatchData () : BatchTrainingData =
        {
            InsideSurfacePoints = ResizeArray<SCNVector3> ()
            OutsideSurfacePoints = ResizeArray<SCNVector3> ()
            FreespacePoints = ResizeArray<SCNVector3> ()
        }
    let mutable batchData = createBatchData ()

    let registerFrames (frames : SdfFrame[]) =
        let v3pool = ArrayPool<Vector3>.Shared

        let transformDiff (x : SdfFrame) (y : SdfFrame) =
            let dt = x.CameraToWorldTransform - y.CameraToWorldTransform
            let drot = abs dt.M11 + abs dt.M22 + abs dt.M33
            let dtrans = (x.CameraToWorldTransform.Translation - y.CameraToWorldTransform.Translation).Length ()
            drot + dtrans
        
        let registered = ConcurrentBag<SdfFrame> ()
        registered.Add frames.[0]
        let needsRegistration =
            frames.[1..]
            |> Array.sortBy (transformDiff frames.[0])

        let icps = ConcurrentDictionary<int, IterativeClosestPoint> ()
            
        let findNearestIcp (f : SdfFrame) : IterativeClosestPoint =
            let rframe =
                registered.ToArray()
                |> Seq.sortBy (transformDiff f)
                |> Seq.head
            printfn "+REG %s WITH %s" f.Title rframe.Title
            icps.GetOrAdd (rframe.FrameIndex, fun _ ->
                let nstaticPoints, staticPointsA = rframe.RentInBoundWorldPoints v3pool
                let staticPoints = Span<Vector3>.op_Implicit (staticPointsA.AsSpan (0, nstaticPoints))
                let icp = IterativeClosestPoint staticPoints
                icp.MaxIterations <- 10
                icp.GoodCorrespondenceDistance <- 0.01f
                icp)

        let r = Parallel.ForEach (needsRegistration, ParallelOptions (MaxDegreeOfParallelism = 1), fun (f : SdfFrame) ->
            NativeJunk.PointRegistration.SayHello ()
            let icp = findNearestIcp f
            let ndynamicPoints, dynamicPointsA = f.RentInBoundWorldPoints v3pool
            let dynamicPoints = dynamicPointsA.AsSpan (0, ndynamicPoints)
            let transform = icp.RegisterPoints (dynamicPoints)
            let goodReg = transform.Translation.Length () < 0.5f
            printfn "-REG %s = %A (%O)" f.Title transform.Translation goodReg
            if goodReg then
                //let mutable itr = transform
                //Matrix4x4.Invert (transform, &itr) |> ignore
                f.Register transform
                registered.Add f
                project.SetChanged "Frames"
            v3pool.Return dynamicPointsA
            ())

        ()

    let registerFramesAsync () : Threading.Tasks.Task =
        if frames.Length > 1 then
            Threading.Tasks.Task.Run (fun () ->
                printfn "REG START"
                for i in 1..4 do
                    registerFrames frames
                    printfn "REG AGAIN!"
                registerFrames frames
                printfn "REG COMPLETE")
        else
            Threading.Tasks.Task.CompletedTask

    //let registerFramesTask = registerFramesAsync ()

    member this.Project = project

    member this.VolumeMin = volumeMin
    member this.VolumeMax = volumeMax
    member this.VolumeCenter = volumeCenter

    member this.Frames = frames

    override this.Count = frames |> Array.sumBy (fun x -> x.PointCount)

    member this.IsClipPointOccupied (clipPoint : Vector4) : bool =
        occupancy.IsOccupied (clipPoint.X, clipPoint.Y, clipPoint.Z)

    member this.ClipWorldPoint (worldPoint : Vector3) =
        Vector4.Transform (worldPoint, inverseClipTransform)

    override this.GetRow (index, _) =
        let inside = (index % 2) = 0
        let mutable fi = StaticRandom.Next(frames.Length)
        while not (frames.[fi].Visible && frames.[fi].HasRows) do
            fi <- StaticRandom.Next(frames.Length)
        let struct (i, o) = frames.[fi].GetRow (inside, volumeCenter, samplingDistance, outputScale, unoccupied, occupancy.NumCells, batchData)
        //printfn "ROW%A D%A = %A" index inside i.[2].[0]
        struct (i, o)

    member this.PopLastTrainingData () =
        let b = batchData
        batchData <- createBatchData ()
        b

