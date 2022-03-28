module NeuralScanner.SceneUtils

open System

open Foundation
open SceneKit
open UIKit

let addFrame (frame : SdfFrame) (sceneView : SCNView) =
    let pointCoords = frame.GetAllPoints ()
    //printfn "POINTS %A" pointCoords

    let source = SCNGeometrySource.FromVertices(pointCoords)
    let element =
        use elemStream = new IO.MemoryStream ()
        use elemWriter = new IO.BinaryWriter (elemStream)
        for i in 0..(pointCoords.Length - 1) do
            elemWriter.Write (i)
        elemWriter.Flush ()
        elemStream.Position <- 0L
        let data = NSData.FromStream (elemStream)
        SCNGeometryElement.FromData(data, SCNGeometryPrimitiveType.Point, nint pointCoords.Length, nint 4)
    let geometry = SCNGeometry.Create([|source|], [|element|])
    let material = SCNMaterial.Create ()
    material.Diffuse.ContentColor <- UIColor.SystemOrange
    element.PointSize <- nfloat 0.01f
    element.MinimumPointScreenSpaceRadius <- nfloat 1.0f
    element.MaximumPointScreenSpaceRadius <- nfloat 5.0f
    geometry.FirstMaterial <- material
    let node = SCNNode.FromGeometry (geometry)
    node.Opacity <- nfloat 0.75
    SCNTransaction.Begin ()
    let root = sceneView.Scene.RootNode
    root.AddChildNode node
    SCNTransaction.Commit ()
    ()

