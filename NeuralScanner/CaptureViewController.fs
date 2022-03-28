namespace NeuralScanner

open System
open Foundation
open CoreGraphics
open ObjCRuntime
open UIKit
open ARKit
open SceneKit

type CaptureViewController (project : Project) =
    inherit UIViewController ()

    let outputDir = project.CaptureDirectory

    let captureButton =
        let b = UIButton.FromType UIButtonType.RoundedRect
        b.SetTitle("Capture", UIControlState.Normal)
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
        base.Title <- "Capture"
        base.TabBarItem.Title <- "Capture"
        base.TabBarItem.Image <- UIImage.GetSystemImage("camera")

    let mutable numCapturedFrames = 0

    let updateProject () =
        project.UpdateCaptures ()
        ()

    override this.LoadView () =
        this.View <- sceneView

    override this.ViewDidLoad () =
        base.ViewDidLoad ()

        this.NavigationItem.RightBarButtonItem <- new UIBarButtonItem(UIBarButtonSystemItem.Done, EventHandler(fun _ _ ->
            async {
                updateProject ()
                this.BeginInvokeOnMainThread (fun _ ->
                    this.DismissViewController (true, null))
            }
            |> Async.Start))

        let bounds = this.View.Bounds
        let buttonHeight = nfloat 88.0
        captureButton.Frame <- CGRect(nfloat 0.0, bounds.Height - buttonHeight - nfloat 128.0, bounds.Width, buttonHeight)
        captureButton.AutoresizingMask <- UIViewAutoresizing.FlexibleWidth ||| UIViewAutoresizing.FlexibleTopMargin
        captureButton.BackgroundColor <- UIColor.SystemOrange
        captureButton.TouchUpInside.Add (fun _ ->
            needsCapture <- true)
        this.View.AddSubview captureButton

        posLabel.Frame <- CGRect(nfloat 0.0, captureButton.Frame.Bottom + nfloat 11.0, bounds.Width, buttonHeight)
        posLabel.AutoresizingMask <- UIViewAutoresizing.FlexibleWidth ||| UIViewAutoresizing.FlexibleTopMargin
        posLabel.BackgroundColor <- UIColor.SystemBackground.ColorWithAlpha(nfloat 0.1)
        posLabel.TextAlignment <- UITextAlignment.Center
        posLabel.Text <- "(0.000, 0.000, 0.000)"
        posLabel.Font <- UIFont.SystemFontOfSize (nfloat 0.5 * buttonHeight)
        this.View.AddSubview posLabel
        
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
            numCapturedFrames <- numCapturedFrames + 1
            let framePrefix = sprintf "Frame%d" numCapturedFrames
            use capturedImage = frame.CapturedImage
            use sceneDepth = frame.SceneDepth
            let cameraResolution = frame.Camera.ImageResolution
            let cameraIntrinsics = frame.Camera.Intrinsics
            let cameraProjection = frame.Camera.ProjectionMatrix
            printfn "COLOR      %A" (capturedImage)
            printfn "DEPTH      %A" sceneDepth
            printfn "INTRINSICS %A" cameraIntrinsics
            printfn "PROJECTION %A" cameraProjection
            printfn "TRANSFORM  %A" cameraTransform
            printfn "POSITION   %A" cameraPosition
            let depthPath = outputPixelBuffer framePrefix "Depth" sceneDepth.DepthMap
            let _ = outputPixelBuffer framePrefix "DepthConfidence" sceneDepth.ConfidenceMap
            let _ = outputPixelBuffer framePrefix "Image" capturedImage
            outputSize framePrefix "Resolution" cameraResolution
            outputNMatrix4 framePrefix "Projection" cameraProjection
            outputNMatrix4 framePrefix "Transform" cameraTransform
            outputNMatrix3 framePrefix "Intrinsics" cameraIntrinsics
            Threading.ThreadPool.QueueUserWorkItem(fun _ ->
                this.PreviewCapture (depthPath)) |> ignore

    member this.PreviewCapture (depthPath) : unit =
        let frame = SdfFrame(depthPath, outputDir, 0.001f, 1.0f)
        let pointCoords = frame.GetAllPoints ()
        printfn "POINTS %A" pointCoords

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
        material.Diffuse.ContentColor <- UIColor.FromRGB(0xCC, 0xCC, 0x00)
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


and CaptureViewDelegate () =
    inherit ARSCNViewDelegate ()





