namespace NeuralScanner

open System
open Foundation
open CoreGraphics
open ObjCRuntime
open UIKit
open ARKit


type CaptureViewController () =
    inherit UIViewController ()

    let captureButton =
        let b = UIButton.FromType UIButtonType.RoundedRect
        b.SetTitle("Capture", UIControlState.Normal)
        b
    let sceneView = new ARSCNView()

    let depthSemantics = ARFrameSemantics.SceneDepth
    let depthOK = ARWorldTrackingConfiguration.SupportsFrameSemantics depthSemantics
    let arConfig =
        let semantics = if depthOK then depthSemantics else ARFrameSemantics.None
        new ARWorldTrackingConfiguration(FrameSemantics = semantics)

    let mutable needsCapture = false

    do
        base.Title <- "Capture"
        base.TabBarItem.Title <- "Capture"
        base.TabBarItem.Image <- UIImage.GetSystemImage("camera")

    override this.LoadView () =
        this.View <- sceneView

    override this.ViewDidLoad () =
        base.ViewDidLoad ()

        let bounds = this.View.Bounds
        let buttonHeight = nfloat 88.0
        captureButton.Frame <- CGRect(nfloat 0.0, bounds.Height - buttonHeight - nfloat 128.0, bounds.Width, buttonHeight)
        captureButton.AutoresizingMask <- UIViewAutoresizing.FlexibleWidth ||| UIViewAutoresizing.FlexibleTopMargin
        captureButton.BackgroundColor <- UIColor.SystemOrangeColor
        captureButton.TouchUpInside.Add (fun _ ->
            needsCapture <- true)
        this.View.AddSubview captureButton
        
        match sceneView.Session with
        | null -> printfn "NO ARKIT"
        | session ->
            session.Run (arConfig)
            session.Delegate <- this

        sceneView.Delegate <- new CaptureViewDelegate ()

    interface IARSessionDelegate

    [<Export("session:didUpdateFrame:")>]
    member this.DidUpdateFrame (session : ARSession, frame : ARFrame) =
        use frame = frame
        if needsCapture then
            use capturedImage = frame.CapturedImage
            let capturedDepth = frame.CapturedDepthData
            let estimatedDepth = frame.EstimatedDepthData
            use sceneDepth = frame.SceneDepth
            let cameraIntrinsics = frame.Camera.Intrinsics
            let cameraProjection = frame.Camera.ProjectionMatrix
            let cameraTransform = frame.Camera.Transform
            printfn "COLOR      %A" (capturedImage)
            printfn "DEPTH      %A" sceneDepth
            printfn "INTRINSICS %A" cameraIntrinsics
            printfn "PROJECTION %A" cameraProjection
            printfn "TRANSFORM  %A" cameraTransform
        ()



and CaptureViewDelegate () =
    inherit ARSCNViewDelegate ()





