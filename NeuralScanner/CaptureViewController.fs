namespace NeuralScanner

open System
open Foundation
open CoreGraphics
open ObjCRuntime
open UIKit
open ARKit
open SceneKit

type CaptureViewController (project : Project) =
    inherit BaseViewController ()

    let outputDir = project.CaptureDirectory

    let captureButton =
        let b = UIButton.FromType UIButtonType.RoundedRect
        b.SetTitle("Scan", UIControlState.Normal)
        b.Enabled <- false
        b.Alpha <- nfloat 0.5
        b
    let posLabel = new UILabel ()
    let sceneView = new ARSCNView()

    let depthSemantics = ARFrameSemantics.SceneDepth
    let depthOK = ARWorldTrackingConfiguration.SupportsFrameSemantics depthSemantics
    let arConfig =
        let semantics = if depthOK then depthSemantics else ARFrameSemantics.None
        new ARWorldTrackingConfiguration(FrameSemantics = semantics)
    //do arConfig.InitialWorldMap <- initialMap

    let mutable needsCapture = false
    let mutable needsBoundingBox = false

    let outputPixelBuffer (prefix : string) (name : string) (buffer : CoreVideo.CVPixelBuffer) : string =
        match buffer.PixelFormatType with
        | CoreVideo.CVPixelFormatType.CV420YpCbCr8BiPlanarFullRange ->
            let path = IO.Path.Combine(outputDir, sprintf "%s_%s.jpg" prefix name)
            let image = CoreImage.CIImage.FromImageBuffer(buffer)
            use uiimage = UIImage.FromImage(image, UIScreen.MainScreen.Scale, UIImageOrientation.Up)
            let scale = uiimage.CurrentScale
            printfn "SCALE = %A" scale
            if uiimage.AsJPEG().Save(path, true) then
                path
            else
                failwithf "Failed to save JPEG"
        | _ ->
            let path = IO.Path.Combine(outputDir, sprintf "%s_%s.pixelbuffer" prefix name)
            use w = IO.File.OpenWrite (path)
            use bw = new IO.BinaryWriter (w)
            let width = buffer.Width
            let height = buffer.Height
            let bytesPerRow = buffer.BytesPerRow
            let pixelFormat = int buffer.PixelFormatType
            let dataSize = buffer.DataSize
            let lockR = buffer.Lock(CoreVideo.CVPixelBufferLock.ReadOnly)
            let bytes = buffer.BaseAddress
            let array : byte[] = Array.zeroCreate (int dataSize)
            Runtime.InteropServices.Marshal.Copy(bytes, array, 0, array.Length)
            let unlockR = buffer.Unlock(CoreVideo.CVPixelBufferLock.ReadOnly)
            bw.Write (0x42_16_90_03)
            bw.Write (int width)
            bw.Write (int height)
            bw.Write (int bytesPerRow)
            bw.Write (int dataSize)
            bw.Write (int pixelFormat)
            bw.Write (array)
            path

    let writeVector4 (w : IO.TextWriter) (name : string) (v : OpenTK.Vector4) =
        w.WriteLine(String.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} {1:0.000000000} {2:0.000000000} {3:0.000000000} {4:0.000000000}", name, v.X, v.Y, v.Z, v.W))

    let outputNMatrix4 (prefix : string) (name : string) (matrix : OpenTK.NMatrix4) =
        let path = IO.Path.Combine(outputDir, sprintf "%s_%s.txt" prefix name)
        use w = new IO.StreamWriter (path)
        writeVector4 w "Row0" matrix.Row0
        writeVector4 w "Row1" matrix.Row1
        writeVector4 w "Row2" matrix.Row2
        writeVector4 w "Row3" matrix.Row3

    let outputNMatrix3 (prefix : string) (name : string) (matrix : OpenTK.NMatrix3) =
        let path = IO.Path.Combine(outputDir, sprintf "%s_%s.txt" prefix name)
        use w = new IO.StreamWriter (path)
        writeVector4 w "Row0" (OpenTK.Vector4(matrix.R0C0, matrix.R0C1, matrix.R0C2, 0f))
        writeVector4 w "Row1" (OpenTK.Vector4(matrix.R1C0, matrix.R1C1, matrix.R1C2, 0f))
        writeVector4 w "Row2" (OpenTK.Vector4(matrix.R2C0, matrix.R2C1, matrix.R2C2, 0f))
        writeVector4 w "Row3" OpenTK.Vector4.UnitW

    let outputSize (prefix : string) (name : string) (size : CGSize) =
        let path = IO.Path.Combine(outputDir, sprintf "%s_%s.txt" prefix name)
        use w = new IO.StreamWriter (path)
        w.WriteLine(String.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.000000000} {1:0.000000000}", size.Width, size.Height))

    do
        base.Title <- "Scan"
        base.TabBarItem.Title <- "Scanner"
        base.TabBarItem.Image <- UIImage.GetSystemImage("camera")

    override this.LoadView () =
        this.View <- sceneView

    override this.ViewDidLoad () =
        base.ViewDidLoad ()

        this.NavigationItem.RightBarButtonItem <- new UIBarButtonItem(UIBarButtonSystemItem.Done, EventHandler(fun _ _ ->
            this.StopUI ()
            this.DismissViewController (true, null)))

        //
        // Start session
        //
        match sceneView.Session with
        | null -> printfn "NO ARKIT"
        | session ->
            session.Run (arConfig)
            session.Delegate <- this

    override this.SubscribeUI () =
        [|
            captureButton.TouchUpInside.Subscribe (fun _ ->
                needsCapture <- true)
        |]

    override this.ViewDidDisappear (a) =
        base.ViewDidDisappear (a)
        this.StopUI ()

    override this.StopUI () =
        base.StopUI ()
        if needsBoundingBox then
            needsBoundingBox <- false
            project.AutoSetBounds ()
        match sceneView.Session with
        | null -> ()
        | session ->
            session.Delegate <- null
            session.Pause ()
        sceneView.Stop (this)

    override this.AddUI view =

        this.View.BackgroundColor <- UIColor.DarkGray

        let fontHeight = nfloat 32.0
        captureButton.BackgroundColor <- UIColor.SystemOrange
        captureButton.Font <- UIFont.SystemFontOfSize (fontHeight)
        captureButton.SetImage(UIImage.GetSystemImage "camera.fill", UIControlState.Normal)
        captureButton.TintColor <- UIColor.White

        posLabel.BackgroundColor <- UIColor.Clear
        posLabel.TextColor <- UIColor.FromWhiteAlpha (nfloat 1.0, nfloat 0.75f)
        posLabel.ShadowColor <- UIColor.FromWhiteAlpha (nfloat 0.0, nfloat 0.75f)
        posLabel.ShadowOffset <- CGSize (2.0, 2.0)
        posLabel.TextAlignment <- UITextAlignment.Center
        posLabel.Text <- "(0.000, 0.000, 0.000)"
        posLabel.Font <- UIFont.SystemFontOfSize (fontHeight)

        //
        // Layout
        //
        let bounds = this.View.Bounds
        let buttonHeight = nfloat 88.0

        this.View.AddSubview captureButton
        captureButton.Frame <- CGRect(nfloat 0.0, bounds.Height - buttonHeight * nfloat 1.5, bounds.Width, buttonHeight)
        captureButton.AutoresizingMask <- UIViewAutoresizing.FlexibleWidth ||| UIViewAutoresizing.FlexibleTopMargin

        posLabel.Frame <- CGRect(nfloat 0.0, captureButton.Frame.Bottom, bounds.Width, buttonHeight * nfloat 0.5)
        posLabel.AutoresizingMask <- UIViewAutoresizing.FlexibleWidth ||| UIViewAutoresizing.FlexibleTopMargin
        this.View.AddSubview posLabel
        [| |]



    interface IARSessionDelegate

    [<Export("session:didUpdateFrame:")>]
    member this.DidUpdateFrame (session : ARSession, frame : ARFrame) =
        use frame = frame
        let cameraTransform = frame.Camera.Transform
        let cameraPosition = cameraTransform.Column3.Xyz
        let posString = sprintf "(%.3f, %.3f, %.3f)" cameraPosition.X cameraPosition.Y cameraPosition.Z
        let canCapture =
            true
            || frame.WorldMappingStatus = ARWorldMappingStatus.Mapped
            || frame.WorldMappingStatus = ARWorldMappingStatus.Extending
        this.BeginInvokeOnMainThread (fun () ->
            posLabel.Text <- posString
            captureButton.Enabled <- canCapture
            captureButton.Alpha <- nfloat (if canCapture then 1.0 else 0.5)
            ())
        if needsCapture && canCapture then
            needsCapture <- false
            let framePrefix = sprintf "Frame%d" project.NewFrameIndex
            use capturedImage = frame.CapturedImage
            use sceneDepth = frame.SceneDepth
            let cameraResolution = frame.Camera.ImageResolution
            let cameraIntrinsics = frame.Camera.Intrinsics
            let cameraProjection = frame.Camera.ProjectionMatrix
            //printfn "COLOR      %A" (capturedImage)
            //printfn "DEPTH      %A" sceneDepth
            //printfn "INTRINSICS %A" cameraIntrinsics
            //printfn "PROJECTION %A" cameraProjection
            //printfn "TRANSFORM  %A" cameraTransform
            //printfn "POSITION   %A" cameraPosition
            if sceneDepth <> null then
                let depthPath = outputPixelBuffer framePrefix "Depth" sceneDepth.DepthMap
                let _ = outputPixelBuffer framePrefix "DepthConfidence" sceneDepth.ConfidenceMap
                ()
            let _ = outputPixelBuffer framePrefix "Image" capturedImage
            outputSize framePrefix "Resolution" cameraResolution
            outputNMatrix4 framePrefix "Projection" cameraProjection
            outputNMatrix4 framePrefix "Transform" cameraTransform
            outputNMatrix3 framePrefix "Intrinsics" cameraIntrinsics
            needsBoundingBox <- true
            Threading.ThreadPool.QueueUserWorkItem(fun _ ->
                let frame = SdfFrame (IO.Path.Combine(outputDir, framePrefix + "_Depth.pixelbuffer"))
                project.AddFrame frame
                let n = frame.CreatePointNode (UIColor.SystemGreen)
                n.Opacity <- nfloat 0.0
                n.Transform <- frame.CameraToWorldSCNMatrix
                SCNTransaction.Begin ()
                SCNTransaction.AnimationDuration <- 1.0
                n.Opacity <- nfloat 0.75
                let root = sceneView.Scene.RootNode
                root.AddChildNode n
                SCNTransaction.Commit ()) |> ignore





