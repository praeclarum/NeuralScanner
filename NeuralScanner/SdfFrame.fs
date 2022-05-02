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

type BatchTrainingData =
    {
        InsideSurfacePoints : ResizeArray<SCNVector3>
        OutsideSurfacePoints : ResizeArray<SCNVector3>
        FreespacePoints : ResizeArray<SCNVector3>
    }

type AxisOccupancy =
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

module PositionEncoding =
    let encodePosition (frameIndex : int) (numFrames : int) (numPositionEncodings : int) (clipPos : Vector3) : Tensor =
        let count = (3 + 6 * numPositionEncodings + numFrames)
        let a = Array.zeroCreate count
        a.[0] <- clipPos.X
        a.[1] <- clipPos.Y
        a.[2] <- clipPos.Z
        for i in 0..(numPositionEncodings-1) do
            let w = float32 (1 <<< i) * MathF.PI
            let j = 6 * i + 3
            a.[j] <- MathF.Cos(w * clipPos.X)
            a.[j+1] <- MathF.Sin(w * clipPos.X)
            a.[j+2] <- MathF.Cos(w * clipPos.Y)
            a.[j+3] <- MathF.Sin(w * clipPos.Y)
            a.[j+4] <- MathF.Cos(w * clipPos.Z)
            a.[j+5] <- MathF.Sin(w * clipPos.Z)
        a.[3 + 6 * numPositionEncodings + frameIndex] <- 1.0f
        Tensor.Array ([| count |], a)


type FrameConfig (visible : bool) =
    inherit Configurable ()
    member val Visible = visible with get, set
    override this.Config = base.Config.Add("visible", this.Visible)

type SdfFrame (depthPath : string) =

    let name = IO.Path.GetFileNameWithoutExtension(depthPath).Replace("_Depth", "")
    let frameIndex = try Int32.Parse (name.Replace("Frame", "")) with _ -> 0
    let title = sprintf "Scan %d" frameIndex

    let filePath fileName = depthPath.Replace ("_Depth.pixelbuffer", "_" + fileName)

    let imagePath = filePath "Image.jpg"

    let configPath = filePath "Config.xml"
    let newConfig () = FrameConfig (visible = true)
    let config =
        if File.Exists configPath then
            try
                Config.Read<FrameConfig> (configPath)
            with ex ->
                printfn "ERROR: %O" ex
                newConfig ()
        else newConfig ()
    let saveConfig () =
        try
            config.Save (configPath)
            printfn "SAVED FRAME CONFIG: %s" configPath
        with ex ->
            printfn "ERROR: %O" ex

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

    let colorBytes : byte[] =
        let image = UIImage.FromFile(imagePath)
        let bytes = Array.zeroCreate (4*width*height)
        do
            let cs = CoreGraphics.CGColorSpace.CreateGenericRgb()
            let bc = new CoreGraphics.CGBitmapContext(bytes, nint width, nint height, nint 8, nint (4*width), cs, CoreGraphics.CGImageAlphaInfo.NoneSkipLast)
            bc.DrawImage(CoreGraphics.CGRect(0.0, 0.0, float width, float height), image.CGImage)
            bc.Flush()
            bc.Dispose()
            cs.Dispose()
        bytes

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
        let text = IO.File.ReadAllText (filePath "Resolution.txt")
        let parts = text.Trim().Split(' ')
        (Single.Parse (parts.[0], CultureInfo.InvariantCulture), Single.Parse (parts.[1], CultureInfo.InvariantCulture))

    let intrinsics =
        let mutable m = loadMatrix (filePath "Intrinsics.txt")
        let colorWidth, _ = resolution
        let iscale = float32 width / float32 colorWidth
        m.M11 <- m.M11 * iscale
        m.M22 <- m.M22 * iscale
        m.M13 <- m.M13 * iscale
        m.M23 <- m.M23 * iscale
        m
    let projection = loadMatrix (filePath "Projection.txt")

    let mutable camToWorldTransform =
        let m = loadMatrix (filePath "Transform.txt")
        Matrix4x4.Transpose(m)

    let mutable camToClipTransform = camToWorldTransform
    let mutable clipToWorldTransform = Matrix4x4.Identity
    let mutable worldToClipTransform = Matrix4x4.Identity

    let index x y = y * width + x
    let colorIndex x y = y * (width * 4) + (x * 4)

    let cameraPosition (x : int) (y : int) depthOffset : Vector3 =
        let depth = -(depths.[index x y] + depthOffset)
        let xc = -(float32 x - intrinsics.M13) * depth / intrinsics.M11
        let yc = (float32 y - intrinsics.M23) * depth / intrinsics.M22
        Vector3(xc, yc, depth)

    let worldPosition (x : int) (y : int) depthOffset : Vector3 =
        let camPos = cameraPosition x y depthOffset
        Vector3.Transform (camPos, camToWorldTransform)

    let clipPosition (x : int) (y : int) depthOffset : Vector3 =
        let camPos = cameraPosition x y depthOffset
        Vector3.Transform(camPos, camToClipTransform)

    let getColor (x : int) (y : int) : Vector3 =
        let ci = colorIndex x y
        let r = colorBytes.[ci]
        let g = colorBytes.[ci+1]
        let b = colorBytes.[ci+2]
        Vector3 (float32 r / 255.0f, float32 g / 255.0f, float32 b / 255.0f)

    //do printfn "FRAME %s center=%A" (IO.Path.GetFileName(depthPath)) (worldPosition (width/2) (height/2) 0.0f)

    static let vector4Shape = [| 4 |]
    static let freespaceShape = [| 1 |]
    static let distanceShape = [| 1 |]

    let mutable inboundIndices = [||]

    let minConfidence = 2uy

    let pointGeometry =
        lazy
            let points = ResizeArray<SceneKit.SCNVector3>()
            let colors = ResizeArray<Vector3>()
            for x in 0..(width-1) do
                for y in 0..(height-1) do
                    let i = index x y
                    let ci = colorIndex x y
                    if confidences.[i] >= minConfidence then
                        let p = cameraPosition x y 0.0f
                        points.Add (SceneKit.SCNVector3 (p.X, p.Y, p.Z))
                        colors.Add (getColor x y)
            let pointCoords = points.ToArray ()
            //SceneKitGeometry.createPointCloudGeometry UIColor.White pointCoords
            SceneKitGeometry.createColoredPointCloudGeometry (colors.ToArray()) pointCoords

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

    let getRandomFreespaceDepthOffset (depth : float32) (x : int) (y : int) : float32 =        
        let mutable sampleDepth = depth * (0.9f + 0.09f*float32 (StaticRandom.NextDouble()))
        let mutable samplePos = clipPosition x y (sampleDepth - depth)
        let mutable n = 0
        while n < 5 && ((abs samplePos.X) > 0.999f || (abs samplePos.Y) > 0.999f || abs samplePos.Z > 0.999f) do
            sampleDepth <- 0.25f*sampleDepth + 0.75f*depth
            samplePos <- clipPosition x y (sampleDepth - depth)
            n <- n + 1
        sampleDepth - depth

    let getRandomCellPoint (i : int) (numOccCells : int) =
        let d = 2.0f / float32 numOccCells
        let m = float32 i * 2.0f / float32 numOccCells - 1.0f
        m + d * (float32 (StaticRandom.NextDouble ()))

    let getRandomUnoccupiedClipPoint (unoccupied : OpenTK.Vector3i[]) (numOccCells : int) : Vector3 =
        let ui = StaticRandom.Next (unoccupied.Length)
        let clipIndex = unoccupied.[ui]
        let clipX = getRandomCellPoint clipIndex.X numOccCells
        let clipY = getRandomCellPoint clipIndex.Y numOccCells
        let clipZ = getRandomCellPoint clipIndex.Z numOccCells
        Vector3 (clipX, clipY, clipZ)

    member this.Title = title
    override this.ToString () = this.Title

    member this.ImagePath = imagePath

    member this.FrameIndex = frameIndex

    member this.DepthPath = depthPath

    member this.CameraToWorldTransform = camToWorldTransform

    member this.CameraToWorldSCNMatrix = camToWorldTransform

    member this.CenterPoint = centerPoint.Value
    member this.MinPoint = fst minMaxPoints.Value
    member this.MaxPoint = snd minMaxPoints.Value

    member this.Visible with get () = config.Visible
                        and set v = config.Visible <- v; saveConfig ()

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

    member this.SetClip (newWorldToClipTransform : Matrix4x4, newClipToWorldTransform : Matrix4x4, occupancy : AxisOccupancy) =
        clipToWorldTransform <- newClipToWorldTransform
        worldToClipTransform <- newWorldToClipTransform
        camToClipTransform <- camToWorldTransform * worldToClipTransform
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

    member this.HasRows = inboundIndices.Length > 0

    member this.GetRow (inside: bool, poi : Vector3, samplingDistance : float32, outputScale : float32, unoccupied : OpenTK.Vector3i[], numOccCells : int, batchData : BatchTrainingData, frameIndex : int, numFrames : int, numPositionEncodings : int) : struct (Tensor[]*Tensor[]) =
        // i = y * width + x
        let index = inboundIndices.[StaticRandom.Next(inboundIndices.Length)]
        let x = index % width
        let y = index / width

        // Half the time inside, half outside
        let rawClipPos, outputSignedDistance, free =
            let isFree = not inside && (StaticRandom.Next (2) = 1)
            let useUnoccupied = false // isFree && (StaticRandom.Next (100) < 50)
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
                            getRandomFreespaceDepthOffset depth x y, 1.0f

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

        let mutable clipPos = rawClipPos
        clipPos.X <- Math.Clamp(clipPos.X, -1.0f, 1.0f)
        clipPos.Y <- Math.Clamp(clipPos.Y, -1.0f, 1.0f)
        clipPos.Z <- Math.Clamp(clipPos.Z, -1.0f, 1.0f)

        let inputs = [| PositionEncoding.encodePosition frameIndex numFrames numPositionEncodings clipPos
                        Tensor.Constant (free, freespaceShape)
                        Tensor.Constant (outputSignedDistance, distanceShape) |]
        struct (inputs, [| |])

    member this.GetPointGeometry () = pointGeometry.Value
    member this.CreatePointNode (color : UIColor) =
        let g = 
            if color <> null then
                let g = pointGeometry.Value.Copy (NSZone.Default) :?> SCNGeometry
                let m = g.FirstMaterial.Copy (NSZone.Default) :?> SCNMaterial
                m.Emission.ContentColor <- color
                g.FirstMaterial <- m
                g
            else pointGeometry.Value
        let node = SCNNode.FromGeometry g
        node

    member this.AddWorldPointsToVoxels (voxels : SdfKit.Voxels) =
        let nx = voxels.NX
        let ny = voxels.NY
        let nz = voxels.NZ
        for x in 0..(width-1) do
            for y in 0..(height-1) do
                let i = index x y
                if confidences.[i] >= minConfidence then
                    let wpos = worldPosition x y 0.0f
                    let vf = (wpos - voxels.Min) / voxels.Size
                    let xi = int (vf.X * float32 nx)
                    let yi = int (vf.Y * float32 ny)
                    let zi = int (vf.Z * float32 nz)
                    if 0 <= xi && xi < nx && 0 <= yi && yi < ny && 0 <= zi && zi < nz then
                        voxels.[xi, yi, zi] <- voxels.[xi, yi, zi] + 1.0f
                        voxels.Colors.[xi, yi, zi] <- voxels.Colors.[xi, yi, zi] + (getColor x y)
        ()

    member this.RentInBoundWorldPoints (pool : Buffers.ArrayPool<Vector3>) : int * Vector3[] =
        let n = inboundIndices.Length
        let r = pool.Rent n
        for ii in 0..(n-1) do
            let index = inboundIndices.[ii]
            let x = index % width
            let y = index / width
            r.[ii] <- worldPosition x y 0.0f
        n, r

    member this.Register (registrationTransform : Matrix4x4) =
        camToWorldTransform <- camToWorldTransform * registrationTransform
        camToClipTransform <- camToWorldTransform * worldToClipTransform
