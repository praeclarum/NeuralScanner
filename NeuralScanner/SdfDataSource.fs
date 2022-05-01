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

type SdfDataSet (project : Project, samplingDistance : float32, outputScale : float32, numPositionEncodings : int) =
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

    let registerFramesOld (frames : SdfFrame[]) =
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
            |> Array.filter (fun x -> x.Visible)
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

    let registerFramesNew (frames : SdfFrame[]) (pass : int) =
        let v3pool = ArrayPool<Vector3>.Shared
        let rented = ResizeArray<_>()

        let frames =
            frames
            |> Array.filter(fun f -> f.Visible)
            |> Array.sortBy(fun f -> f.FrameIndex)

        let icp =
            let nstaticPoints, staticPointsA = frames.[0].RentInBoundWorldPoints v3pool
            rented.Add(staticPointsA)
            let staticPoints = Span<Vector3>.op_Implicit (staticPointsA.AsSpan (0, nstaticPoints))
            IterativeClosestPoint staticPoints
        icp.MaxIterations <- 15
        icp.GoodCorrespondenceDistance <- 0.005f
        for i in 1..(frames.Length - 1) do
            let f = frames.[i]
            let ndynamicPoints, dynamicPointsA = f.RentInBoundWorldPoints v3pool
            let dynamicPoints = dynamicPointsA.AsSpan (0, ndynamicPoints)
            let transform = icp.RegisterPoints (dynamicPoints)
            let goodReg = transform.Translation.Length () < 0.2f
            printfn "-REG %s = %A (%O)" f.Title transform.Translation goodReg
            if goodReg then
                icp.AddStaticPoints (Span<Vector3>.op_Implicit dynamicPoints)
                //let mutable itr = transform
                //Matrix4x4.Invert (transform, &itr) |> ignore
                if pass < 0 then
                    for j in i..(frames.Length - 1) do
                        frames.[j].Register transform
                else
                    frames.[i].Register transform
                project.SetChanged "Frames"
            v3pool.Return dynamicPointsA
            ()

    let registerFramesOpenGR (frames : SdfFrame[]) (pass : int) =
        let v3pool = ArrayPool<Vector3>.Shared
        let rented = ResizeArray<_>()

        let frames =
            frames
            |> Array.filter(fun f -> f.Visible)
            |> Array.sortBy(fun f -> f.FrameIndex)

        let score =
            let nstaticPoints, staticPointsA = frames.[0].RentInBoundWorldPoints v3pool
            rented.Add(staticPointsA)
            let staticPoints = Span<Vector3>.op_Implicit (staticPointsA.AsSpan (0, nstaticPoints))

            let ndynamicPoints, dynamicPointsA = frames.[4].RentInBoundWorldPoints v3pool
            rented.Add(dynamicPointsA)
            let dynamicPoints = dynamicPointsA.AsSpan (0, ndynamicPoints)

            //let mutable mat = Matrix4x4.Identity
            //NativeJunk.PointRegistration.OpenGR(staticPoints, dynamicPoints, &mat)
            NativeJunk.PointRegistration.IterativeClosestPoint(staticPoints, dynamicPoints)

        for i in 0..(rented.Count - 1) do
            v3pool.Return rented.[i]
        ()

    let registerFramesAsync () : Threading.Tasks.Task =
        if frames.Length > 1 then
            Threading.Tasks.Task.Run (fun () ->
                let n = 1
                for i in 1..n do
                    printfn "REG START PASS %d/%d" i n
                    registerFramesOpenGR frames i
                    printfn "REG END PASS %d/%d" i n
                printfn "REG COMPLETE")
        else
            Threading.Tasks.Task.CompletedTask

    //let registerFramesTask = registerFramesAsync ()
    //member this.WaitForRegistration () = registerFramesTask.Wait()

    //let testOpenGR() =
    //    let staticPoints =
    //        [|
    //            Vector3(1f, 0f, 0f)
    //            Vector3(0f, 1f, 0f)
    //        |]
    //    let dynamicPoints =
    //        [|
    //            Vector3(1.1f, 0f, 0f)
    //            Vector3(0.1f, 1f, 0f)
    //        |]
    //    let mutable mat = Matrix4x4.Identity
    //    let score = NativeJunk.PointRegistration.OpenGR(Span<Vector3>.op_Implicit (staticPoints.AsSpan()), dynamicPoints.AsSpan(), &mat)
    //    ()
    //do testOpenGR ()

    member this.WaitForRegistration () = ()

    member this.Project = project

    member this.VolumeMin = volumeMin
    member this.VolumeMax = volumeMax
    member this.VolumeCenter = volumeCenter

    member this.NumPositionEncodings = numPositionEncodings

    member this.Frames = frames

    override this.Count = frames |> Array.sumBy (fun x -> x.PointCount)

    member this.IsClipPointOccupied (clipPoint : Vector3) : bool =
        occupancy.IsOccupied (clipPoint.X, clipPoint.Y, clipPoint.Z)

    member this.ClipWorldPoint (worldPoint : Vector3) : Vector3 =
        Vector3.Transform (worldPoint, inverseClipTransform)

    override this.GetRow (index, _) =
        let inside = (index % 2) = 0
        let mutable fi = StaticRandom.Next(frames.Length)
        while not (frames.[fi].Visible && frames.[fi].HasRows) do
            fi <- StaticRandom.Next(frames.Length)
        let struct (i, o) = frames.[fi].GetRow (inside, volumeCenter, samplingDistance, outputScale, unoccupied, occupancy.NumCells, batchData, fi, frames.Length, numPositionEncodings)
        //printfn "ROW%A D%A = %A" index inside i.[2].[0]
        struct (i, o)

    member this.PopLastTrainingData () =
        let b = batchData
        batchData <- createBatchData ()
        b

