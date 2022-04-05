namespace NeuralScanner

open System
open System.Runtime.InteropServices
open System.Collections.Concurrent
open System.IO
open System.Numerics
open System.Globalization

open SceneKit
open MetalTensors

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
        let tr = project.ClipTransform
        let tr4 = Matrix4x4(tr.M11, tr.M12, tr.M13, tr.M14,
                            tr.M21, tr.M22, tr.M23, tr.M24,
                            tr.M31, tr.M32, tr.M33, tr.M34,
                            tr.M41, tr.M42, tr.M43, tr.M44)
        let mutable itr = tr
        itr.Invert ()
        let itr4 = Matrix4x4(itr.M11, itr.M12, itr.M13, itr.M14,
                             itr.M21, itr.M22, itr.M23, itr.M24,
                             itr.M31, itr.M32, itr.M33, itr.M34,
                             itr.M41, itr.M42, itr.M43, itr.M44)
        for f in frames do
            f.SetClip (itr4, tr4, occupancy)
        tr4, itr4
    let unoccupied = occupancy.GetUnoccupied ()

    let createBatchData () : BatchTrainingData =
        {
            InsideSurfacePoints = ResizeArray<SCNVector3> ()
            OutsideSurfacePoints = ResizeArray<SCNVector3> ()
            FreespacePoints = ResizeArray<SCNVector3> ()
        }
    let mutable batchData = createBatchData ()

    let registerFrame (icp : SdfKit.IterativeClosestPoint) (dynamicFrame : SdfFrame) : Matrix4x4 =
        let v3pool = System.Buffers.ArrayPool<Vector3>.Shared
        let ndynamicPoints, dynamicPointsA = dynamicFrame.RentInBoundWorldPoints v3pool
        let dynamicPoints = dynamicPointsA.AsSpan (0, ndynamicPoints)
        let transform = icp.RegisterPoints (dynamicPoints)
        v3pool.Return dynamicPointsA
        transform

    let registerFrames () : unit =
        if frames.Length > 1 then
            let f0 = frames.[0]
            let v3pool = System.Buffers.ArrayPool<Vector3>.Shared
            let nstaticPoints, staticPointsA = f0.RentInBoundWorldPoints v3pool
            let staticPoints = Span<Vector3>.op_Implicit (staticPointsA.AsSpan (0, nstaticPoints))
            let icp = SdfKit.IterativeClosestPoint staticPoints

            for fi in 1..3 do
                let f = frames.[1]
                let ndynamicPoints, dynamicPointsA = f.RentInBoundWorldPoints v3pool
                let dynamicPoints = dynamicPointsA.AsSpan (0, ndynamicPoints)
                let transform = icp.RegisterPoints (dynamicPoints)
                let mutable itransform = transform
                if Matrix4x4.Invert (transform, &itransform) then
                    printfn "ITRANS: %A" itransform.Translation
                    f.Register itransform
                v3pool.Return dynamicPointsA

    do registerFrames ()

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
        let fi = StaticRandom.Next(frames.Length)
        let struct (i, o) = frames.[fi].GetRow (inside, volumeCenter, samplingDistance, outputScale, unoccupied, occupancy.NumCells, batchData)
        //printfn "ROW%A D%A = %A" index inside i.[2].[0]
        struct (i, o)

    member this.PopLastTrainingData () =
        let b = batchData
        batchData <- createBatchData ()
        b

