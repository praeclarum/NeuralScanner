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

    let createSolidMeshNode (mesh : SdfKit.Mesh) : SCNNode =
        if mesh.Vertices.Length > 0 && mesh.Triangles.Length > 0 then
            let vertsSource =
                mesh.Vertices
                |> Array.map (fun v -> SCNVector3(v.X, v.Y, v.Z))
                |> SCNGeometrySource.FromVertices
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
