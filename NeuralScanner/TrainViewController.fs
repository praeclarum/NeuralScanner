namespace NeuralScanner

open System
open Foundation
open CoreGraphics
open ObjCRuntime
open UIKit
open ARKit
open SdfKit
open System.Numerics
open CoreML

#nowarn "9"
open FSharp.NativeInterop

type TrainViewController () =
    inherit UIViewController (Title = "Train")

    //let trainer = Trainer ()

    let graph = new LossGraphView ()

    override this.ViewDidLoad() =
        base.ViewDidLoad()
        this.View.BackgroundColor <- UIColor.SystemBackground
        graph.Frame <- this.View.Bounds
        this.View.AddSubview (graph)
        //trainer.BatchTrained.Add (fun (progress, totalTrained, loss) ->
        //    graph.AddLoss (progress, loss))

        //System.Threading.ThreadPool.QueueUserWorkItem(fun _ -> trainer.Train ()) |> ignore
        //System.Threading.ThreadPool.QueueUserWorkItem(fun _ -> trainer.GenerateMesh ()) |> ignore

