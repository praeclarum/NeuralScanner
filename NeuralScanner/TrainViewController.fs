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

    let trainer = Trainer ()

    let graph = new LossGraphView ()

    override this.ViewDidLoad() =
        base.ViewDidLoad()
        this.View.BackgroundColor <- UIColor.SystemBackground
        graph.Frame <- this.View.Bounds
        this.View.AddSubview (graph)
        trainer.BatchTrained.Add (fun (progress, totalTrained, loss) ->
            graph.AddLoss (progress, loss))

        System.Threading.ThreadPool.QueueUserWorkItem(fun _ -> trainer.Train ()) |> ignore
        //System.Threading.ThreadPool.QueueUserWorkItem(fun _ -> trainer.GenerateMesh ()) |> ignore


and LossGraphView () =
    inherit UIView ()

    let valueLabel = new UILabel(Frame = base.Bounds,
                                 AutoresizingMask = UIViewAutoresizing.FlexibleDimensions)
    do base.AddSubview valueLabel

    let progressView = new UIProgressView (Frame = base.Bounds,
                                           AutoresizingMask = UIViewAutoresizing.FlexibleDimensions)
    do base.AddSubview progressView

    let losses = ResizeArray<float32> ()

    member this.AddLoss (progress : float32, loss : float32) =
        this.BeginInvokeOnMainThread (fun _ ->
            progressView.Progress <- float32 progress
            losses.Add loss
            this.SetNeedsDisplay ()
            valueLabel.Text <- sprintf "%.4f %.1f%%" loss (progress * 100.0f))

    override this.Draw (rect) =
        UIColor.SystemBackground.SetFill ()
        UIGraphics.RectFill (rect)
        if losses.Count > 1 then
            let bounds = this.Bounds

            let dxdi = bounds.Width / nfloat (float (losses.Count - 1))
            let dydl = bounds.Height / nfloat 0.03

            UIColor.SystemYellow.SetFill ()
            let c = UIGraphics.GetCurrentContext ()
            for i in 0..(losses.Count - 1) do
                let x = nfloat (float i) * dxdi
                let y = bounds.Height - nfloat (losses.[i]) * dydl
                if i = 0 then
                    c.MoveTo (x, y)
                else
                    c.AddLineToPoint (x, y)
            c.AddLineToPoint (bounds.Width, bounds.Height)
            c.AddLineToPoint (nfloat 0.0, bounds.Height)
            c.ClosePath ()
            c.FillPath ()



