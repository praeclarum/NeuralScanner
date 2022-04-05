module NeuralScanner.SdfFrameRegistration

open System
open System.Buffers
open System.Numerics

let private fpool = System.Buffers.ArrayPool<float32>.Shared
let private v3pool = System.Buffers.ArrayPool<Vector3>.Shared

let meanPoint (points : Span<Vector3>) : Vector3 =
    let n = points.Length
    let mutable x = 0.0f
    let mutable y = 0.0f
    let mutable z = 0.0f
    for i in 0..(n - 1) do
        let p = points.[i]
        x <- x + p.X
        y <- y + p.Y
        z <- z + p.Z
    let s = 1.0f / float32 n
    Vector3 (x * s, y * s, z * s)

let registerPoints (staticPointsM : Memory<Vector3>) (dynamicPointsM : Memory<Vector3>) : Matrix4x4 =
    let mutable finalTransform = Matrix4x4.Identity

    // Build a KD Tree for the statics
    let ns = staticPointsM.Length
    let staticPoints = staticPointsM.Span
    let staticTree = SdfKit.KdTree (Span<Vector3>.op_Implicit staticPoints)

    // Create a copy of dynamic
    let nd = dynamicPointsM.Length
    let dpairA = v3pool.Rent nd
    let dpair = dpairA.AsSpan (0, nd)
    let ddistA = fpool.Rent nd
    let ddist = ddistA.AsSpan (0, nd)
    let dcopyA = v3pool.Rent nd
    let dcopy = dcopyA.AsSpan (0, nd)

    // Loop until good enough
    let mutable goodEnough = false
    while not goodEnough do
        // Find pairs for d points in statics
        let dynamicPoints = dynamicPointsM.Span
        let mutable sumDist = 0.0f
        for i in 0..(nd - 1) do
            let dp = dynamicPoints.[i]
            dpair.[i] <- staticTree.Search dp
            let dist = Vector3.Distance (dpair.[i], dp)
            ddist.[i] <- dist
            sumDist <- sumDist + dist

        // Use the distance statistics to filter out bad pairs
        let meanDist = sumDist / float32 nd

        failwithf "TODO: Filter pairs, create cross matrix, SVD, apply transform"

    v3pool.Return dcopyA
    fpool.Return ddistA
    v3pool.Return dpairA
    finalTransform
    

let registerFrame (staticFrame : SdfFrame) (dynamicFrame : SdfFrame) : Matrix4x4 =
    let nstaticPoints, staticPointsA = staticFrame.RentInBoundWorldPoints v3pool
    let ndynamicPoints, dynamicPointsA = dynamicFrame.RentInBoundWorldPoints v3pool
    let staticPoints = staticPointsA.AsMemory (0, nstaticPoints)
    let dynamicPoints = dynamicPointsA.AsMemory (0, ndynamicPoints)
    let transform = registerPoints staticPoints dynamicPoints
    v3pool.Return staticPointsA
    v3pool.Return dynamicPointsA
    transform


