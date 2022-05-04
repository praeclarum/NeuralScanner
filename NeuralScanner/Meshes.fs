module NeuralScanner.Meshes

open System
open System.Runtime.InteropServices
open System.Collections.Concurrent
open System.IO
open System.Numerics
open System.Globalization

open SceneKit
open MetalTensors

let getSamplingGrid (data : SdfDataSet) =
    let volumeDims = data.VolumeMax - data.VolumeMin
    let minDim = min (min volumeDims.X volumeDims.Y) volumeDims.Z
    let project = data.Project
    let nPerDim = project.Settings.Resolution / minDim
    let nx = ((int (round (nPerDim * volumeDims.X)) + 1) / 2) * 2
    let ny = ((int (round (nPerDim * volumeDims.Y)) + 1) / 2) * 2
    let nz = ((int (round (nPerDim * volumeDims.Z)) + 1) / 2) * 2
    nx, ny, nz

let generateSolidVoxels (model : Model) (data : SdfDataSet) (progress : float32 -> unit) =
    let nx, ny, nz = getSamplingGrid data
    let totalPoints = nx*ny*nz
    let mutable numPoints = 0

    let tpool = System.Buffers.ArrayPool<Tensor>.Shared
    let tapool = System.Buffers.ArrayPool<Tensor[]>.Shared

    let sdf (x : Memory<Vector3>) (y : Memory<Vector4>) =
        let n = x.Length
        let batchTensors = ResizeArray<_> (n)
        let batchOutputs = ResizeArray<_> (n)
        let x = x.Span
        let yspan = y.Span
        for i in 0..(n - 1) do
            //let p = x.[i] - data.VolumeCenter
            let p = data.ClipWorldPoint x.[i]
            if data.IsClipPointOccupied p then
                let input = PositionEncoding.encodePosition 0 data.Frames.Length data.NumPositionEncodings p
                let inputs = tpool.Rent 1
                inputs.[0] <- input
                batchTensors.Add inputs
                batchOutputs.Add i
            else
                yspan.[i] <- ProjectDefaults.outsideSdf
        let nin = batchTensors.Count
        if nin > 0 then
            let nbatch = batchTensors.Count
            let results = model.Predict (batchTensors.ToArray (), nbatch)
            for i in 0..(nbatch - 1) do
                tpool.Return batchTensors.[i]
            //tapool.Return batchTensors
            let res : float32[] = Array.zeroCreate 4
            for i in 0..(nin-1) do
                let r = results.[i].[0]
                r.CopyTo(res.AsSpan())
                let yvec = Vector4(res.[0]*0.5f + 0.5f, res.[1]*0.5f + 0.5f, res.[2]*0.5f + 0.5f, res.[3])
                //if numPoints < totalPoints / 100 then
                //    printfn "%g" yvec.W
                yspan.[batchOutputs.[i]] <- yvec
        numPoints <- numPoints + y.Length
        let p = float32 numPoints / float32 totalPoints
        progress p
        //printfn "NN Sample Progress: %.1f" (p * 100.0)

    let voxels = SdfKit.Voxels.SampleSdf (sdf, data.VolumeMin, data.VolumeMax, nx, ny, nz, batchSize = ProjectDefaults.batchSize, maxDegreeOfParallelism = 2)
    voxels.ClipToBounds ()
    voxels

let generateRoughVoxels (data : SdfDataSet) (progress : float32 -> unit) =
    let nx, ny, nz = getSamplingGrid data

    let voxels = SdfKit.Voxels (data.VolumeMin, data.VolumeMax, nx, ny, nz)

    for f in data.Frames do
        if f.Visible then
            f.AddWorldPointsToVoxels voxels

    let vs = voxels.Values
    let cs = voxels.Colors
    for xi in 0..(nx - 1) do
        for yi in 0..(ny - 1) do
            for zi in 0..(nz - 1) do
                if vs.[xi, yi, zi] >= 1.0f then
                    cs.[xi, yi, zi] <- cs.[xi, yi, zi] / vs.[xi, yi, zi]
                    vs.[xi, yi, zi] <- -1.0f
                else
                    vs.[xi, yi, zi] <- 1.0f

    voxels.ClipToBounds ()
    voxels

let meshFromVoxels (voxels : SdfKit.Voxels) =
    let mesh = SdfKit.MarchingCubes.CreateMesh (voxels, 0.0f, step = 1)
    mesh
