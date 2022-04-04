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

    let frames = project.GetVisibleFrames ()
    let count = depthFiles |> Seq.sumBy (fun x -> 1)
    do if count = 0 then failwithf "No files in %s" dataDirectory
    let framesMin =
        let m = 1.0e6f*Vector3.One
        frames |> Array.fold (fun a x -> Vector3.Min (x.MinPoint, a)) m
    let framesMax =
        let m = -1.0e6f*Vector3.One
        frames |> Array.fold (fun a x -> Vector3.Max (x.MaxPoint, a)) m

    let meanCenter = project.Settings.ClipTranslation
    let volumeMin, volumeMax =
        let vmin = meanCenter - project.Settings.ClipScale
        let vmax = meanCenter + project.Settings.ClipScale
        vmin, vmax
        
    let volumeCenter = (volumeMin + volumeMax) * 0.5f

    let occupancy = AxisOccupancy.Create 16

    //do for f in frames do f.FindInBoundPoints(volumeMin, volumeMax)
    let inverseClipTransform =
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
            f.SetBoundsInverseTransform (itr4, tr4, occupancy)
        itr4

    let unoccupied = occupancy.GetUnoccupied ()

    let createBatchData () : BatchTrainingData =
        {
            InsideSurfacePoints = ResizeArray<SCNVector3> ()
            OutsideSurfacePoints = ResizeArray<SCNVector3> ()
            FreespacePoints = ResizeArray<SCNVector3> ()
        }
    let mutable batchData = createBatchData ()

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

