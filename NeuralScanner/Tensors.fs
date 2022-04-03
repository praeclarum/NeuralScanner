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

module SceneKitGeometry =

    let createPointCloudGeometry (color : UIColor) (pointCoords : SCNVector3[]) =
        if pointCoords.Length = 0 then
            failwithf "No points provided"
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
        element.PointSize <- nfloat 0.01f
        element.MinimumPointScreenSpaceRadius <- nfloat 0.1f
        element.MaximumPointScreenSpaceRadius <- nfloat 5.0f
        let material = SCNMaterial.Create ()
        material.Emission.ContentColor <- color
        material.ReadsFromDepthBuffer <- true
        material.WritesToDepthBuffer <- true
        geometry.FirstMaterial <- material
        geometry

    let createPointCloudNode (color : UIColor) (pointCoords : SCNVector3[]) =
        if pointCoords.Length = 0 then
            SCNNode.Create ()
        else
            let g = createPointCloudGeometry color pointCoords
            let n = SCNNode.FromGeometry g
            n

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

    let mutable camToClipTransform = transform
    let mutable clipToWorldTransform = transform

    let clipPosition (x : int) (y : int) depthOffset : Vector4 =
        let camPos = cameraPosition x y depthOffset
        // World = Transform * Camera
        // World = Camera * Transform'
        //let testResult = Vector4.Transform(Vector4.UnitW, transform)
        Vector4.Transform(camPos, camToClipTransform)

    let centerPos = worldPosition (width/2) (height/2) 0.0f

    do printfn "FRAME %s center=%g, %g, %g" (IO.Path.GetFileName(depthPath)) centerPos.X centerPos.Y centerPos.Z

    let vector4Shape = [| 4 |]
    let freespaceShape = [| 1 |]
    let distanceShape = [| 1 |]

    let mutable inboundIndices = [||]

    let minConfidence = 2uy

    let pointCoords =
        lazy
            let points = ResizeArray<SceneKit.SCNVector3>()
            for x in 0..(width-1) do
                for y in 0..(height-1) do
                    let i = index x y
                    if confidences.[i] >= minConfidence then
                        let p = worldPosition x y 0.0f
                        points.Add (SceneKit.SCNVector3(p.X, p.Y, p.Z))
            points.ToArray ()

    let pointGeometry =
        lazy
            let pointCoords = pointCoords.Value
            SceneKitGeometry.createPointCloudGeometry UIColor.White pointCoords

    let centerPoint =
        lazy
            let sx = width/2 - 1
            let ex = sx + 3
            let sy = height/2 - 1
            let ey = sy + 3
            let mutable sum = Vector3.Zero
            let mutable n = 0
            for x in sx..ex do
                for y in sy..ey do
                    let i = index x y
                    if confidences.[i] >= minConfidence then
                        let p = worldPosition x y 0.0f
                        sum <- Vector3 (sum.X + p.X, sum.Y + p.Y, sum.Z + p.Z)
                        n <- n + 1
            if n > 0 then sum / float32 n
            else sum

    let minMaxPoints =
        lazy
            let mutable minv = Vector3.Zero
            let mutable maxv = Vector3.Zero
            let mutable n = 0
            for x in 0..(width-1) do
                for y in 0..(height-1) do
                    let i = index x y
                    if confidences.[i] >= minConfidence then
                        let p = worldPosition x y 0.0f
                        if n = 0 then
                            minv <- Vector3(p.X, p.Y, p.Z)
                            maxv <- minv
                        else
                            minv <- Vector3(min p.X minv.X, min p.Y minv.Y, min p.Z minv.Z)
                            maxv <- Vector3(max p.X maxv.X, max p.Y maxv.Y, max p.Z maxv.Z)
                        n <- n + 1
            minv, maxv

    let getRandomFreespaceDepthOffset (depth : float32) (x : int) (y : int) (poi : Vector3) : float32 =        
        let mutable sampleDepth = depth * float32 (StaticRandom.NextDouble())
        let mutable samplePos = clipPosition x y (sampleDepth - depth)
        let mutable n = 0
        while n < 5 && ((abs samplePos.X) > 1.1f || (abs samplePos.Y) > 1.1f || abs samplePos.Z > 1.1f) do
            sampleDepth <- 0.25f*sampleDepth + 0.75f*depth
            samplePos <- clipPosition x y (sampleDepth - depth)
            n <- n + 1
        sampleDepth - depth

    let getRandomCellPoint (i : int) (numOccCells : int) =
        let d = 2.0f / float32 numOccCells
        let m = float32 i * 2.0f / float32 numOccCells - 1.0f
        m + d * (float32 (StaticRandom.NextDouble ()))

    let getRandomUnoccupiedClipPoint (unoccupied : OpenTK.Vector3i[]) (numOccCells : int) : Vector4 =
        let ui = StaticRandom.Next (unoccupied.Length)
        let clipIndex = unoccupied.[ui]
        let clipX = getRandomCellPoint clipIndex.X numOccCells
        let clipY = getRandomCellPoint clipIndex.Y numOccCells
        let clipZ = getRandomCellPoint clipIndex.Z numOccCells
        Vector4 (clipX, clipY, clipZ, 1.0f)

    member this.DepthPath = depthPath

    member this.CenterPoint = centerPoint.Value
    member this.MinPoint = fst minMaxPoints.Value
    member this.MaxPoint = snd minMaxPoints.Value

    member this.FindInBoundPoints (min : Vector3, max : Vector3) =
        let inbounds = ResizeArray<_>()
        for x in 0..(width-1) do
            for y in 0..(height-1) do
                let i = index x y
                if confidences.[i] >= minConfidence then
                    let p = worldPosition x y 0.0f
                    if p.X >= min.X && p.Y >= min.Y && p.Z >= min.Z &&
                       p.X <= max.X && p.Y <= max.Y && p.Z <= max.Z then
                        inbounds.Add(i)
        inboundIndices <- inbounds.ToArray ()

    member this.SetBoundsInverseTransform (newWorldToClipTransform : Matrix4x4, newClipToWorldTransform : Matrix4x4, occupancy : AxisOccupancy) =
        camToClipTransform <- transform * newWorldToClipTransform
        clipToWorldTransform <- newClipToWorldTransform
        let inbounds = ResizeArray<_>()
        for x in 0..(width-1) do
            for y in 0..(height-1) do
                let i = index x y
                if confidences.[i] >= minConfidence then
                    let p = clipPosition x y 0.0f
                    if p.X >= -1.0f && p.Y >= -1.0f && p.Z >= -1.0f &&
                       p.X <= 1.0f && p.Y <= 1.0f && p.Z <= 1.0f then
                        occupancy.AddPoint (p.X, p.Y, p.Z)
                        inbounds.Add(i)
        inboundIndices <- inbounds.ToArray ()

    member this.PointCount = inboundIndices.Length

    member this.GetRow (inside: bool, poi : Vector3, samplingDistance : float32, outputScale : float32, unoccupied : OpenTK.Vector3i[], numOccCells : int, batchData : BatchTrainingData) : struct (Tensor[]*Tensor[]) =
        // i = y * width + x
        let index = inboundIndices.[StaticRandom.Next(inboundIndices.Length)]
        let x = index % width
        let y = index / width

        // Half the time inside, half outside
        let clipPos, outputSignedDistance, free =
            let isFree = not inside && (StaticRandom.Next (2) = 1)
            let useUnoccupied = isFree && (StaticRandom.Next (100) < 50)
            if useUnoccupied then
                let cp = getRandomUnoccupiedClipPoint unoccupied numOccCells
                let wpos = Vector4.Transform (cp, clipToWorldTransform)
                let swpos = SCNVector3 (wpos.X, wpos.Y, wpos.Z)
                batchData.FreespacePoints.Add swpos
                cp, 0.1f * outputScale, 1.0f
            else
                let depthOffset, free =
                    if inside then
                        // Inside Surface
                        abs (randn () * samplingDistance), 0.0f
                    else
                        // Outside
                        if not isFree then
                            // Ouside Surface
                            -abs (randn () * samplingDistance), 0.0f
                        else
                            // Freespace
                            let depth = depths.[index]
                            getRandomFreespaceDepthOffset depth x y poi, 1.0f

                let wpos = worldPosition x y depthOffset
                let swpos = SCNVector3 (wpos.X, wpos.Y, wpos.Z)
                if depthOffset >= 0.0f then
                    batchData.InsideSurfacePoints.Add swpos
                elif free > 0.5f then
                    batchData.FreespacePoints.Add swpos
                else
                    batchData.OutsideSurfacePoints.Add swpos

                //let pos = wpos - Vector4(poi, 1.0f)
                let clipPos = clipPosition x y depthOffset
                let outputSignedDistance = -depthOffset * outputScale
                clipPos, outputSignedDistance, free


        let inputs = [| Tensor.Array (vector4Shape, clipPos.X, clipPos.Y, clipPos.Z, 1.0f)
                        Tensor.Constant (free, freespaceShape)
                        Tensor.Constant (outputSignedDistance, distanceShape) |]
        struct (inputs, [| |])

    member this.GetPointGeometry () = pointGeometry.Value
    member this.CreatePointNode (color : UIColor) =
        let g = pointGeometry.Value.Copy (NSZone.Default) :?> SCNGeometry
        let m = g.FirstMaterial.Copy (NSZone.Default) :?> SCNMaterial
        m.Emission.ContentColor <- color
        g.FirstMaterial <- m
        let node = SCNNode.FromGeometry g
        node


and BatchTrainingData =
    {
        InsideSurfacePoints : ResizeArray<SCNVector3>
        OutsideSurfacePoints : ResizeArray<SCNVector3>
        FreespacePoints : ResizeArray<SCNVector3>
    }

and AxisOccupancy =
    {
        XAxis : bool[,]
        YAxis : bool[,]
        ZAxis : bool[,]
    }
    static member Create (n : int) =
        {
            XAxis = Array2D.zeroCreate n n
            YAxis = Array2D.zeroCreate n n
            ZAxis = Array2D.zeroCreate n n
        }
    member this.NumCells = this.ZAxis.GetLength (0)
    member this.AddPoint (clipX : float32, clipY : float32, clipZ : float32) =
        let n = this.NumCells
        let s = float32 n / 2.0f
        let xi = int ((clipX + 1.0f) * s)
        let yi = int ((clipY + 1.0f) * s)
        let zi = int ((clipZ + 1.0f) * s)
        if 0 <= xi && xi < n && 0 <= yi && yi < n && 0 <= zi && zi < n then
            this.XAxis.[yi, zi] <- true
            this.YAxis.[zi, xi] <- true
            this.ZAxis.[xi, yi] <- true
    member this.IsOccupied (clipX : float32, clipY : float32, clipZ : float32) =
        let n = this.NumCells
        let s = float32 n / 2.0f
        let xi = int ((clipX + 1.0f) * s)
        let yi = int ((clipY + 1.0f) * s)
        let zi = int ((clipZ + 1.0f) * s)
        if 0 <= xi && xi < n && 0 <= yi && yi < n && 0 <= zi && zi < n then
            this.XAxis.[yi, zi] && this.YAxis.[zi, xi] && this.ZAxis.[xi, yi]
        else
            false
    member this.GetUnoccupied () : OpenTK.Vector3i[] =
        let n = this.NumCells
        //let occ = ResizeArray<OpenTK.Vector3i> (n*n*n)
        let unocc = ResizeArray<OpenTK.Vector3i> (n*n*n)
        for xi in 0..(n - 1) do
            for yi in 0..(n - 1) do
                if this.ZAxis.[xi, yi] then
                    for zi in 0..(n - 1) do
                        if this.XAxis.[yi, zi] && this.YAxis.[zi, xi] then
                            //occ.Add (OpenTK.Vector3i (xi, yi, zi))
                            ()
                        else
                            unocc.Add (OpenTK.Vector3i (xi, yi, zi))
                else
                    for zi in 0..(n - 1) do
                        unocc.Add (OpenTK.Vector3i (xi, yi, zi))
        unocc.ToArray ()
