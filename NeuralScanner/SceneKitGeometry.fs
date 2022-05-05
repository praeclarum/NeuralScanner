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

    let matrixFromSCNMatrix4 (tr : SCNMatrix4) =
        Matrix4x4 (tr.M11, tr.M12, tr.M13, tr.M14,
                   tr.M21, tr.M22, tr.M23, tr.M24,
                   tr.M31, tr.M32, tr.M33, tr.M34,
                   tr.M41, tr.M42, tr.M43, tr.M44)

    let matrixToSCNMatrix4 (tr : Matrix4x4) =
        SCNMatrix4 (tr.M11, tr.M12, tr.M13, tr.M14,
                    tr.M21, tr.M22, tr.M23, tr.M24,
                    tr.M31, tr.M32, tr.M33, tr.M34,
                    tr.M41, tr.M42, tr.M43, tr.M44)

    let createPointCloudGeometryWithSources (sources : SCNGeometrySource[]) (pointCoords : SCNVector3[]) : SCNGeometry =
        if pointCoords.Length = 0 then
            failwithf "No points provided"
        let element =
            let elemStream = new IO.MemoryStream ()
            let elemWriter = new IO.BinaryWriter (elemStream)
            for i in 0..(pointCoords.Length - 1) do
                elemWriter.Write (i)
            elemWriter.Flush ()
            elemStream.Position <- 0L
            let data = NSData.FromStream (elemStream)
            SCNGeometryElement.FromData(data, SCNGeometryPrimitiveType.Point, nint pointCoords.Length, nint 4)
        let geometry = SCNGeometry.Create(sources, [|element|])
        element.PointSize <- nfloat 0.01f
        element.MinimumPointScreenSpaceRadius <- nfloat 0.1f
        element.MaximumPointScreenSpaceRadius <- nfloat 5.0f
        let material = SCNMaterial.Create ()
        material.ReadsFromDepthBuffer <- true
        material.WritesToDepthBuffer <- true
        geometry.FirstMaterial <- material
        geometry

    let createPointCloudGeometry (color : UIColor) (pointCoords : SCNVector3[]) : SCNGeometry =
        if pointCoords.Length = 0 then
            failwithf "No points provided"
        let source = SCNGeometrySource.FromVertices(pointCoords)
        let geometry = createPointCloudGeometryWithSources [|source|] pointCoords
        let material = geometry.FirstMaterial
        material.Emission.ContentColor <- color
        geometry

    let createColoredPointCloudGeometry (colors : Vector3[]) (pointCoords : SCNVector3[]) : SCNGeometry =
        if pointCoords.Length = 0 then
            failwithf "No points provided"
        let source = SCNGeometrySource.FromVertices(pointCoords)
        let csource =
            let elemStream = new IO.MemoryStream ()
            let elemWriter = new IO.BinaryWriter (elemStream)
            for i in 0..(colors.Length - 1) do
                let c = colors.[i]
                elemWriter.Write c.X
                elemWriter.Write c.Y
                elemWriter.Write c.Z
            elemWriter.Flush ()
            elemStream.Position <- 0L
            let data = NSData.FromStream (elemStream)
            SCNGeometrySource.FromData(data, SCNGeometrySourceSemantics.Color, nint colors.Length, true, nint 3, nint 4, nint 0, nint (3*4))
        let geometry = createPointCloudGeometryWithSources [|source;csource|] pointCoords
        let material = geometry.FirstMaterial
        material.Diffuse.ContentColor <- UIColor.White
        material.LightingModelName <- SCNLightingModel.Constant
        geometry

    let createPointCloudNode (color : UIColor) (pointCoords : SCNVector3[]) =
        if pointCoords.Length = 0 then
            SCNNode.Create ()
        else
            let g = createPointCloudGeometry color pointCoords
            let n = SCNNode.FromGeometry g
            n

    let createColoredPointCloudNode (colors : Vector3[]) (pointCoords : SCNVector3[]) =
        if pointCoords.Length = 0 then
            SCNNode.Create ()
        else
            let g = createColoredPointCloudGeometry colors pointCoords
            let n = SCNNode.FromGeometry g
            n

    let getMeshComponents (mesh : SdfKit.Mesh) : SdfKit.Mesh[] =
        let numTris = mesh.Triangles.Length/3
        let remTris = Collections.Generic.HashSet<_>(seq { 0..(numTris-1) })
        let queueTris = Collections.Generic.Queue<_>()
        let vf = Collections.Generic.Dictionary<int, ResizeArray<int>> ()
        for ti in 0 .. (numTris - 1) do
            for j in 0..2 do
                let vi = mesh.Triangles.[ti*3 + j]
                let vfs =
                    match vf.TryGetValue(vi) with
                    | true, x -> x
                    | _ ->
                        let x = ResizeArray<int>()
                        vf.Add(vi, x)
                        x
                vfs.Add ti
        let meshes = ResizeArray<_> ()
        let subTris = ResizeArray<int> ()
        let addMesh () =
            // Build new triangles
            let newTris = Array.zeroCreate (subTris.Count * 3)
            let newVerts = ResizeArray<Vector3> (subTris.Count * 3)
            let newVertIndex = Collections.Generic.Dictionary<int, int> ()
            let newColors = ResizeArray<Vector3> (subTris.Count * 3)
            let newNorms = ResizeArray<Vector3> (subTris.Count * 3)
            for nti in 0..(subTris.Count-1) do
                let oti = subTris.[nti]
                for j in 0..2 do
                    // Renumber the vertices
                    let ovi = mesh.Triangles.[oti*3 + j]
                    let nvi =
                        match newVertIndex.TryGetValue ovi with
                        | true, x -> x
                        | _ ->
                            let x = newVerts.Count
                            newVerts.Add(mesh.Vertices.[ovi])
                            newColors.Add(mesh.Colors.[ovi])
                            newNorms.Add(mesh.Normals.[ovi])
                            newVertIndex.Add(ovi, x)
                            x
                    newTris.[nti*3 + j] <- nvi
            meshes.Add(new SdfKit.Mesh(newVerts.ToArray (), newColors.ToArray (), newNorms.ToArray (), newTris))
            subTris.Clear ()
            queueTris.Clear ()
            ()
        while remTris.Count > 0 do
            let ti = remTris |> Seq.head
            queueTris.Enqueue ti
            while queueTris.Count > 0 do
                let ti = queueTris.Dequeue ()
                if remTris.Remove ti then
                    subTris.Add(ti)
                    for j in 0..2 do
                        let vi = mesh.Triangles.[ti*3 + j]
                        vf.[vi] |> Seq.iter (fun sti ->
                            if remTris.Contains sti && not (queueTris.Contains sti) then
                                queueTris.Enqueue sti)
                        ()
            addMesh ()
        meshes |> Seq.sortByDescending (fun x -> x.Triangles.Length) |> Array.ofSeq

    let createSolidMeshNode (mesh : SdfKit.Mesh) : SCNNode =
        if mesh.Vertices.Length > 0 && mesh.Triangles.Length > 0 then
            let vertsSource =
                mesh.Vertices
                |> Array.map (fun v -> SCNVector3(v.X, v.Y, v.Z))
                |> SCNGeometrySource.FromVertices
            //let tcSource =
            //    mesh.Vertices
            //    |> Array.map (fun v -> CoreGraphics.CGPoint.Empty)
            //    |> SCNGeometrySource.FromTextureCoordinates
            let normsSource =
                mesh.Normals
                |> Array.map (fun v -> SCNVector3(v.X, v.Y, v.Z))
                |> SCNGeometrySource.FromNormals
            let colorsSource =
                let elemStream = new IO.MemoryStream ()
                let elemWriter = new IO.BinaryWriter (elemStream)
                for i in 0..(mesh.Colors.Length - 1) do
                    let c = mesh.Colors.[i]
                    elemWriter.Write (c.X)
                    elemWriter.Write (c.Y)
                    elemWriter.Write (c.Z)
                    elemWriter.Write (1.0f)
                elemWriter.Flush ()
                elemStream.Position <- 0L
                let data = NSData.FromStream (elemStream)
                SCNGeometrySource.FromData(data, SCNGeometrySourceSemantics.Color, nint mesh.Colors.Length, true, nint 4, nint 4, nint 0, nint (4*4))
            let element =
                let elemStream = new IO.MemoryStream ()
                let elemWriter = new IO.BinaryWriter (elemStream)
                for i in 0..(mesh.Triangles.Length - 1) do
                    elemWriter.Write (mesh.Triangles.[i])
                elemWriter.Flush ()
                elemStream.Position <- 0L
                let data = NSData.FromStream (elemStream)
                SCNGeometryElement.FromData(data, SCNGeometryPrimitiveType.Triangles, nint (mesh.Triangles.Length / 3), nint 4)
            let geometry = SCNGeometry.Create([|vertsSource;normsSource;colorsSource|], [|element|])
            let material = SCNMaterial.Create ()
            //material.Diffuse.ContentColor <- UIColor.White
            geometry.FirstMaterial <- material
            SCNNode.FromGeometry(geometry)
        else
            SCNNode.Create ()
