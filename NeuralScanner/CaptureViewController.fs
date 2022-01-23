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

    let outputDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)

    let outputPixelBuffer (prefix : string) (name : string) (buffer : CoreVideo.CVPixelBuffer) =
        match buffer.PixelFormatType with
        | CoreVideo.CVPixelFormatType.CV420YpCbCr8BiPlanarFullRange ->
            let path = IO.Path.Combine(outputDir, sprintf "%s_%s.png" prefix name)
            let image = CoreImage.CIImage.FromImageBuffer(buffer)
            let uiimage = UIImage.FromImage(image)
            uiimage.AsPNG().Save(path, true) |> ignore
        | _ ->
            let path = IO.Path.Combine(outputDir, sprintf "%s_%s.pixels" prefix name)
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
        ()

    let outputNMatrix4 (prefix : string) (name : string) (matrix : OpenTK.NMatrix4) =
        let path = IO.Path.Combine(outputDir, sprintf "%s_%s.txt" prefix name)
        use w = new IO.StreamWriter (path)
        w.WriteLine("Row0 {0}", matrix.Row0)
        w.WriteLine("Row1 {0}", matrix.Row1)
        w.WriteLine("Row2 {0}", matrix.Row2)
        w.WriteLine("Row3 {0}", matrix.Row3)

    let outputNMatrix3 (prefix : string) (name : string) (matrix : OpenTK.NMatrix3) =
        let path = IO.Path.Combine(outputDir, sprintf "%s_%s.txt" prefix name)
        use w = new IO.StreamWriter (path)
        w.WriteLine("Row0 {0}", OpenTK.Vector4(matrix.R0C0, matrix.R0C1, matrix.R0C2, 0f))
        w.WriteLine("Row1 {0}", OpenTK.Vector4(matrix.R1C0, matrix.R1C1, matrix.R1C2, 0f))
        w.WriteLine("Row3 {0}", OpenTK.Vector4(matrix.R2C0, matrix.R2C1, matrix.R2C2, 0f))
        w.WriteLine("Row4 {0}", OpenTK.Vector4.UnitW)

    do
        base.Title <- "Capture"
        base.TabBarItem.Title <- "Capture"
        base.TabBarItem.Image <- UIImage.GetSystemImage("camera")

    let mutable numCapturedFrames = 0

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
            needsCapture <- false
            numCapturedFrames <- numCapturedFrames + 1
            let framePrefix = sprintf "Frame%d" numCapturedFrames
            use capturedImage = frame.CapturedImage
            use sceneDepth = frame.SceneDepth
            let cameraIntrinsics = frame.Camera.Intrinsics
            let cameraProjection = frame.Camera.ProjectionMatrix
            let cameraTransform = frame.Camera.Transform
            printfn "COLOR      %A" (capturedImage)
            printfn "DEPTH      %A" sceneDepth
            printfn "INTRINSICS %A" cameraIntrinsics
            printfn "PROJECTION %A" cameraProjection
            printfn "TRANSFORM  %A" cameraTransform
            outputPixelBuffer framePrefix "SceneDepth" sceneDepth.DepthMap
            outputPixelBuffer framePrefix "SceneDepthConfidence" sceneDepth.ConfidenceMap
            outputPixelBuffer framePrefix "Image" capturedImage
            outputNMatrix4 framePrefix "CameraProjection" cameraProjection
            outputNMatrix4 framePrefix "CameraTransform" cameraTransform
            outputNMatrix3 framePrefix "CameraIntrinsics" cameraIntrinsics
        ()



and CaptureViewDelegate () =
    inherit ARSCNViewDelegate ()





