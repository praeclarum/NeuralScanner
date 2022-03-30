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

type LossGraphView () =
    inherit UIView (Frame = CGRect (0.0, 0.0, 320.0, 200.0), Opaque = false)

    let valueLabel = new UILabel(Frame = CGRect (0.0, 0.0, 320.0, 32.0),
                                 AutoresizingMask = (UIViewAutoresizing.FlexibleBottomMargin ||| UIViewAutoresizing.FlexibleWidth),
                                 ShadowColor = UIColor.SystemBackground,
                                 ShadowOffset = CGSize (1.0, 1.0),
                                 TextAlignment = UITextAlignment.Right,
                                 Font = UIFont.BoldSystemFontOfSize (nfloat 24.0))
    do base.AddSubview valueLabel

    //let progressView = new UIProgressView (Frame = base.Bounds,
    //                                       AutoresizingMask = UIViewAutoresizing.FlexibleDimensions,
    //                                       Alpha = nfloat 0.0)
    //do base.AddSubview progressView

    let losses = ResizeArray<float32> ()

    override this.SizeThatFits (size) =
        CGSize (320.0f, 200.0f)

    override this.IntrinsicContentSize =
        CGSize (320.0f, 200.0f)

    member this.SetLosses(loss : float32[]) =
        if loss.Length > 0 then
            this.BeginInvokeOnMainThread (fun _ ->
                losses.Clear ()
                losses.AddRange (loss)
                this.SetNeedsDisplay ()
                valueLabel.Text <- sprintf "%.5f" loss.[loss.Length - 1])

    member this.AddLoss (batchSize: int, totalTrained : int, loss : float32) =
        this.BeginInvokeOnMainThread (fun _ ->
            //progressView.Progress <- float32 progress
            //progressView.Alpha <- if 1e-6f <= progress && progress <= (1.0f-1e-6f)
            //                      then nfloat 1.0
            //                      else  nfloat 0.0
            losses.Add loss
            this.SetNeedsDisplay ()
            valueLabel.Text <- sprintf "%.5f" loss)

    override this.Draw (rect) =
        UIColor.Clear.SetFill ()
        UIGraphics.RectFill (rect)
        if losses.Count > 1 then
            let bounds = this.Bounds

            let maxn = 256
            let n = min maxn losses.Count
            let b = losses.Count - n

            let mutable sum = 0.0f
            for i in 0..(n - 1) do
                sum <- sum + losses.[b + i]
            let mean = sum / float32 n

            let dxdi = bounds.Width / nfloat (float (n - 1))
            let dydl = bounds.Height / nfloat (mean * 2.0f)

            UIColor.SystemYellow.ColorWithAlpha(nfloat 0.75f).SetFill ()
            let c = UIGraphics.GetCurrentContext ()
            for i in 0..(n - 1) do
                let x = nfloat (float i) * dxdi
                let y = bounds.Height - nfloat (losses.[b + i]) * dydl
                if i = 0 then
                    c.MoveTo (x, y)
                else
                    c.AddLineToPoint (x, y)
            c.AddLineToPoint (bounds.Width, bounds.Height)
            c.AddLineToPoint (nfloat 0.0, bounds.Height)
            c.ClosePath ()
            c.FillPath ()



