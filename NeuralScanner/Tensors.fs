namespace NeuralScanner

open System
open System.Runtime.InteropServices
open System.IO
open System.Numerics
open System.Globalization

open Foundation
open MetalTensors
open SceneKit
open UIKit

[<AutoOpen>]
module MathOps =
    let randn () =
        let u1 = StaticRandom.NextDouble ()
        let u2 = StaticRandom.NextDouble ()
        float32 (Math.Sqrt(-2.0 * Math.Log (u1)) * Math.Cos(2.0 * Math.PI * u2))



type SdfFrame (depthPath : string) =

    let width, height, depths =
        let f = File.OpenRead (depthPath)
        let r = new BinaryReader (f)
        let magic = r.ReadInt32 ()
        let width = r.ReadInt32 ()
        let height = r.ReadInt32 ()
        let stride = r.ReadInt32 ()
        let dataSize = r.ReadInt32 ()
        let pixelFormat = r.ReadInt32 ()
        let len = width * height
        assert(len = dataSize/4)
        let depths : float32[] = Array.zeroCreate len
        let span = MemoryMarshal.AsBytes(depths.AsSpan())
        let n = f.Read (span)
        assert(n = dataSize)
        r.Close()
        width, height, depths

    let confidences =
        let path = depthPath.Replace("_Depth", "_DepthConfidence")
        let f = File.OpenRead (path)
        let r = new BinaryReader (f)
        let magic = r.ReadInt32 ()
        let width = r.ReadInt32 ()
        let height = r.ReadInt32 ()
        let stride = r.ReadInt32 ()
        let dataSize = r.ReadInt32 ()
        let pixelFormat = r.ReadInt32 ()
        let len = width * height
        assert(len = dataSize)
        let confs : byte[] = Array.zeroCreate len
        let span = MemoryMarshal.AsBytes(confs.AsSpan())
        let n = f.Read (span)
        assert(n = dataSize)
        r.Close()
        confs

    let pointCount = depths.Length

    let loadMatrix (path : string) =
        let rows =
            File.ReadAllLines(path)
            |> Array.map (fun x ->
                x.Split(' ')
                |> Seq.skip 1
                |> Seq.map (fun y -> Single.Parse (y, CultureInfo.InvariantCulture))
                |> Array.ofSeq)                
        Matrix4x4(rows.[0].[0], rows.[0].[1], rows.[0].[2], rows.[0].[3],
                  rows.[1].[0], rows.[1].[1], rows.[1].[2], rows.[1].[3],
                  rows.[2].[0], rows.[2].[1], rows.[2].[2], rows.[2].[3],
                  rows.[3].[0], rows.[3].[1], rows.[3].[2], rows.[3].[3])

    let resolution =
        let text = File.ReadAllText (depthPath.Replace ("_Depth.pixelbuffer", "_Resolution.txt"))
        let parts = text.Trim().Split(' ')
        (Single.Parse (parts.[0], CultureInfo.InvariantCulture), Single.Parse (parts.[1], CultureInfo.InvariantCulture))

    let intrinsics =
        let mutable m = loadMatrix (depthPath.Replace ("_Depth.pixelbuffer", "_Intrinsics.txt"))
        let colorWidth, _ = resolution
        let iscale = float32 width / float32 colorWidth
        m.M11 <- m.M11 * iscale
        m.M22 <- m.M22 * iscale
        m.M13 <- m.M13 * iscale
        m.M23 <- m.M23 * iscale
        m
    let projection = loadMatrix (depthPath.Replace ("_Depth.pixelbuffer", "_Projection.txt"))
    let transform =
        let m = loadMatrix (depthPath.Replace ("_Depth.pixelbuffer", "_Transform.txt"))
        Matrix4x4.Transpose(m)

    let index x y = y * width + x

    let cameraPosition (x : int) (y : int) depthOffset : Vector4 =
        let depth = -(depths.[index x y] + depthOffset)
        let xc = -(float32 x - intrinsics.M13) * depth / intrinsics.M11
        let yc = (float32 y - intrinsics.M23) * depth / intrinsics.M22
        Vector4(xc, yc, depth, 1.0f)

    let worldPosition (x : int) (y : int) depthOffset : Vector4 =
        let camPos = cameraPosition x y depthOffset
        // World = Transform * Camera
        // World = Camera * Transform'
        //let testResult = Vector4.Transform(Vector4.UnitW, transform)
        Vector4.Transform(camPos, transform)

    let centerPos = worldPosition (width/2) (height/2) 0.0f

    do printfn "FRAME %s center=%g, %g, %g" (IO.Path.GetFileName(depthPath)) centerPos.X centerPos.Y centerPos.Z

    let vector3Shape = [| 3 |]
    let freespaceShape = [| 1 |]
    let distanceShape = [| 1 |]

    let mutable inboundIndices = [||]

    let pointCoords =
        lazy
            let points = ResizeArray<SceneKit.SCNVector3>()
            for x in 0..(width-1) do
                for y in 0..(height-1) do
                    let i = index x y
                    if confidences.[i] > 0uy then
                        let p = worldPosition x y 0.0f
                        points.Add (SceneKit.SCNVector3(p.X, p.Y, p.Z))
            points.ToArray ()

    let pointGeometry =
        lazy
            let pointCoords = pointCoords.Value
            let source = SCNGeometrySource.FromVertices(pointCoords)
            let element =
                let elemStream = new IO.MemoryStream ()
                let elemWriter = new IO.BinaryWriter (elemStream)
                for i in 0..(pointCoords.Length - 1) do
                    elemWriter.Write (i)
                elemWriter.Flush ()
                elemStream.Position <- 0L
                let data = NSData.FromStream (elemStream)
                SCNGeometryElement.FromData(data, SCNGeometryPrimitiveType.Point, nint pointCoords.Length, nint 4)
            let geometry = SCNGeometry.Create([|source|], [|element|])
            let material = SCNMaterial.Create ()
            material.Diffuse.ContentColor <- UIColor.White
            material.ReadsFromDepthBuffer <- false
            material.WritesToDepthBuffer <- true
            element.PointSize <- nfloat 0.01f
            element.MinimumPointScreenSpaceRadius <- nfloat 1.0f
            element.MaximumPointScreenSpaceRadius <- nfloat 5.0f
            geometry.FirstMaterial <- material
            geometry

    member this.DepthPath = depthPath

    member this.CenterPoint =
        let sx = width/2 - 1
        let ex = sx + 3
        let sy = height/2 - 1
        let ey = sy + 3
        let mutable sum = Vector3.Zero
        let mutable n = 0
        for x in sx..ex do
            for y in 0..(height-1) do
                let i = index x y
                if confidences.[i] > 0uy then
                    let p = worldPosition x y 0.0f
                    sum <- Vector3 (sum.X + p.X, sum.Y + p.Y, sum.Z + p.Z)
                    n <- n + 1
        if n > 0 then sum / float32 n
        else sum

    member this.FindInBoundPoints (min : Vector3, max : Vector3) =
        let inbounds = ResizeArray<_>()
        for x in 0..(width-1) do
            for y in 0..(height-1) do
                let i = index x y
                if confidences.[i] > 0uy then
                    let p = worldPosition x y 0.0f
                    if p.X >= min.X && p.Y >= min.Y && p.Z >= min.Z &&
                       p.X <= max.X && p.Y <= max.Y && p.Z <= max.Z then
                        inbounds.Add(i)
        inboundIndices <- inbounds.ToArray ()

    member this.PointCount = inboundIndices.Length

    member this.GetRow (inside: bool, poi : Vector3, samplingDistance : float32, outputScale : float32) : struct (Tensor[]*Tensor[]) =
        // i = y * width + x
        let index = inboundIndices.[StaticRandom.Next(inboundIndices.Length)]
        let x = index % width
        let y = index / width

        // Half the time inside, half outside
        let depthOffset, free =
            if inside then
                abs (randn () * samplingDistance), 0.0f
            else
                // Outside
                if StaticRandom.Next(2) = 0 then
                    // Surface
                    -abs (randn () * samplingDistance), 0.0f
                else
                    // Freespace
                    let depth = depths.[index]
                    float32 (-StaticRandom.NextDouble()) * depth, 1.0f

        let pos = worldPosition x y depthOffset - Vector4(poi, 1.0f)
        let outputSignedDistance = -depthOffset * outputScale
        let inputs = [| Tensor.Array(vector3Shape, pos.X, pos.Y, pos.Z)
                        Tensor.Array(freespaceShape, free)
                        Tensor.Array(distanceShape, outputSignedDistance) |]
        struct (inputs, [| |])

    member this.GetPointGeometry () = pointGeometry.Value
    member this.CreatePointNode (color : UIColor) =
        let g = pointGeometry.Value.Copy (NSZone.Default) :?> SCNGeometry
        let m = g.FirstMaterial.Copy (NSZone.Default) :?> SCNMaterial
        m.Diffuse.ContentColor <- color
        g.FirstMaterial <- m
        let node = SCNNode.FromGeometry g
        node



