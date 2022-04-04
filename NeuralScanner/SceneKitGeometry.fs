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

    let createPointCloudGeometry (color : UIColor) (pointCoords : SCNVector3[]) : SCNGeometry =
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

    let createSolidMeshNode (mesh : SdfKit.Mesh) : SCNNode =
        if mesh.Vertices.Length > 0 && mesh.Triangles.Length > 0 then
            let vertsSource =
                mesh.Vertices
                |> Array.map (fun v -> SCNVector3(v.X, v.Y, v.Z))
                |> SCNGeometrySource.FromVertices
            let normsSource =
                mesh.Normals
                |> Array.map (fun v -> SCNVector3(-v.X, -v.Y, -v.Z))
                |> SCNGeometrySource.FromNormals
            let element =
                let elemStream = new IO.MemoryStream ()
                let elemWriter = new IO.BinaryWriter (elemStream)
                for i in 0..(mesh.Triangles.Length - 1) do
                    elemWriter.Write (mesh.Triangles.[i])
                elemWriter.Flush ()
                elemStream.Position <- 0L
                let data = NSData.FromStream (elemStream)
                SCNGeometryElement.FromData(data, SCNGeometryPrimitiveType.Triangles, nint (mesh.Triangles.Length / 3), nint 4)
            let geometry = SCNGeometry.Create([|vertsSource;normsSource|], [|element|])
            let material = SCNMaterial.Create ()
            material.Diffuse.ContentColor <- UIColor.White
            geometry.FirstMaterial <- material
            SCNNode.FromGeometry(geometry)
        else
            SCNNode.Create ()
