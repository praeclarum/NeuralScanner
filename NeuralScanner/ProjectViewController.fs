namespace NeuralScanner


open System
open System.Collections.Concurrent

open Foundation
open UIKit
open SceneKit

open Praeclarum.AutoLayout


type ProjectViewController (project : Project) =
    inherit BaseViewController ()

    // Services
    let trainingService = TrainingServices.getForProject (project)

    // UI
    let sceneView = new SCNView (UIScreen.MainScreen.Bounds,
                                 BackgroundColor = UIColor.SystemBackground)
    do sceneView.AutoenablesDefaultLighting <- true

    let lossView = new LossGraphView ()
    do lossView.SetLosses (trainingService.Losses)

    let sliderToLearningRate (v : float32) =
        let logLR = float32 (int (10.0f * ((1.0f-v)*6.0f + 1.0f) + 0.5f)) / 10.0f
        MathF.Pow (10.0f, -logLR)
    let learningRateToSlider (lr : float32) =
        1.0f - ((-MathF.Log10 (lr)-1.0f) / 6.0f)
    let learningRateSlider = new ValueSlider ("Learning Rate", "{0:0.0000000}",
                                              0.0f, 1.0f,
                                              sliderToLearningRate,
                                              learningRateToSlider)

    let previewResolutionSlider = new ValueSlider ("Resolution", "{0:0}",
                                                   16.0f, 512.0f,
                                                   (fun x -> MathF.Round x),
                                                   (fun x -> x))
    let previewButton = UIButton.FromType(UIButtonType.RoundedRect)
    do
        previewButton.TranslatesAutoresizingMaskIntoConstraints <- false
        previewButton.SetTitle("Preview", UIControlState.Normal)
        previewButton.SetImage(UIImage.GetSystemImage "cube", UIControlState.Normal)

    let captureButton = UIButton.FromType(UIButtonType.RoundedRect)
    do
        captureButton.SetTitle("Scan Object", UIControlState.Normal)
        captureButton.SetImage(UIImage.GetSystemImage "viewfinder", UIControlState.Normal)
    let labelCaptureInfo = new UILabel (Text = "Object not scanned")
    let capturePanel = new UIStackView (Axis = UILayoutConstraintAxis.Horizontal, Spacing = nfloat 44.0)
    do capturePanel.AddArrangedSubview (captureButton)
    do capturePanel.AddArrangedSubview (labelCaptureInfo)

    let trainButton = UIButton.FromType(UIButtonType.RoundedRect)
    let pauseTrainButton = UIButton.FromType(UIButtonType.RoundedRect)
    let resetTrainButton = UIButton.FromType(UIButtonType.RoundedRect)
    do
        trainButton.SetTitle("Train", UIControlState.Normal)
        trainButton.SetImage(UIImage.GetSystemImage "record.circle", UIControlState.Normal)
        pauseTrainButton.SetTitle("Pause Training", UIControlState.Normal)
        pauseTrainButton.SetImage(UIImage.GetSystemImage "pause.circle", UIControlState.Normal)
        resetTrainButton.SetTitle("Reset Training", UIControlState.Normal)
        resetTrainButton.SetImage(UIImage.GetSystemImage "arrow.counterclockwise.circle", UIControlState.Normal)
    let trainButtons = new UIStackView (Axis = UILayoutConstraintAxis.Horizontal, Spacing = nfloat 44.0)
    do trainButtons.AddArrangedSubview trainButton
    do trainButtons.AddArrangedSubview pauseTrainButton
    do trainButtons.AddArrangedSubview resetTrainButton
    let nameField = new UITextField (Font = UIFont.PreferredTitle1, Placeholder = "Name", Text = project.Name)

    let previewProgress = new UIProgressView (Alpha = nfloat 0.0f, TranslatesAutoresizingMaskIntoConstraints = false)

    let scene = SCNScene.Create()
    do sceneView.Scene <- scene
    let rootNode = scene.RootNode
    let pointCloudNode = new SCNNode (Name = "PointCloud")
    do rootNode.AddChildNode pointCloudNode
    let framePointNodes = ConcurrentDictionary<string, SCNNode> ()
    let mutable previewNeuralMeshNode : SCNNode option = None

    member this.HandleCapture () =
        let captureVC = new CaptureViewController (project)
        let captureNC = new UINavigationController (captureVC)
        captureNC.ModalPresentationStyle <- UIModalPresentationStyle.PageSheet
        this.PresentViewController(captureNC, true, null)
        ()

    override this.ViewDidLoad () =
        base.ViewDidLoad ()

    override this.AddUI view =
        capturePanel.TranslatesAutoresizingMaskIntoConstraints <- false

        sceneView.Frame <- this.View.Bounds
        sceneView.AutoresizingMask <- UIViewAutoresizing.FlexibleDimensions
        sceneView.AllowsCameraControl <- true

        lossView.TranslatesAutoresizingMaskIntoConstraints <- false

        nameField.TextAlignment <- UITextAlignment.Center
        nameField.TranslatesAutoresizingMaskIntoConstraints <- false

        trainButtons.TranslatesAutoresizingMaskIntoConstraints <- false

        view.AddSubview (sceneView)
        view.AddSubview (lossView)
        view.AddSubview nameField
        view.AddSubview trainButtons
        view.AddSubview capturePanel
        view.AddSubview learningRateSlider
        view.AddSubview previewProgress
        view.AddSubview previewButton
        view.AddSubview previewResolutionSlider

        [|
            nameField.LayoutTop == previewProgress.LayoutBottom
            nameField.LayoutLeading == view.SafeAreaLayoutGuide.LayoutLeading
            nameField.LayoutTrailing == view.SafeAreaLayoutGuide.LayoutTrailing
            lossView.LayoutLeft == view.LayoutLeft
            lossView.LayoutRight == view.LayoutRight
            lossView.LayoutBottom == view.LayoutBottom
            lossView.LayoutTop == view.SafeAreaLayoutGuide.LayoutBottom - 88.0
            trainButtons.LayoutCenterX == lossView.LayoutCenterX
            trainButtons.LayoutBottom == lossView.LayoutTop
            learningRateSlider.LayoutCenterX == lossView.LayoutCenterX
            learningRateSlider.LayoutBottom == trainButtons.LayoutTop
            learningRateSlider.LayoutWidth == lossView.LayoutWidth * 0.5
            capturePanel.LayoutCenterX == lossView.LayoutCenterX
            capturePanel.LayoutTop == nameField.LayoutBottom + 11.0
            previewButton.LayoutCenterX == capturePanel.LayoutCenterX
            previewButton.LayoutTop == capturePanel.LayoutBottom + 11.0
            previewResolutionSlider.LayoutTop == previewButton.LayoutBottom
            previewResolutionSlider.LayoutCenterX == previewButton.LayoutCenterX
            previewResolutionSlider.LayoutWidth == view.LayoutWidth * 0.5
            previewProgress.LayoutTop == view.SafeAreaLayoutGuide.LayoutTop
            previewProgress.LayoutHeight == 4
            previewProgress.LayoutLeft == view.SafeAreaLayoutGuide.LayoutLeft
            previewProgress.LayoutRight == view.SafeAreaLayoutGuide.LayoutRight
        |]

    override this.UpdateUI () =
        labelCaptureInfo.Text <-
            if project.NumCaptures = 0 then "Object not scanned"
            else sprintf "%d depth scans" project.NumCaptures
        captureButton.Hidden <- project.NumCaptures > 0
        if trainingService.IsTraining then
            trainButton.Enabled <- false
            pauseTrainButton.Enabled <- true
        else
            trainButton.Enabled <- project.NumCaptures > 0
            pauseTrainButton.Enabled <- false
        if not learningRateSlider.UserInteracting then
            learningRateSlider.Value <- project.Settings.LearningRate
        Threading.ThreadPool.QueueUserWorkItem (fun _ -> this.UpdatePointCloud ()) |> ignore

    override this.SubscribeUI () =
        nameField.ShouldReturn <- fun x -> x.ResignFirstResponder() |> ignore; false
        [|
            project.Changed.Subscribe (fun _ ->
                this.BeginInvokeOnMainThread (fun _ ->
                    this.UpdateUI ()))
            trainingService.Changed.Subscribe (fun _ ->
                this.BeginInvokeOnMainThread (fun _ ->
                    this.UpdateUI ()))
            trainingService.BatchTrained.Subscribe (fun (progress, totalTrained, loss) ->
                this.BeginInvokeOnMainThread (fun _ ->
                    lossView.AddLoss (progress, loss)))

            learningRateSlider.ValueChanged.Subscribe (fun lr ->
                project.Settings.LearningRate <- lr
                project.SetModified "Settings.LearningRate")
            previewResolutionSlider.ValueChanged.Subscribe (fun r ->
                let r = int r
                project.Settings.ResolutionX <- r
                project.Settings.ResolutionY <- r
                project.Settings.ResolutionZ <- r
                project.SetModified "Settings.Resolution")

            nameField.EditingChanged.Subscribe (fun _ ->
                project.Name <- nameField.Text)

            captureButton.TouchUpInside.Subscribe(fun _ -> this.HandleCapture())
            trainButton.TouchUpInside.Subscribe (fun _ ->
                trainingService.Run ()
                this.UpdateUI ())
            pauseTrainButton.TouchUpInside.Subscribe (fun _ ->
                trainingService.Pause ()
                this.UpdateUI ())
            resetTrainButton.TouchUpInside.Subscribe (fun _ ->
                trainingService.Pause ()
                trainingService.Reset ()
                this.UpdateUI ())
            previewButton.TouchUpInside.Subscribe (fun _ ->
                let wasTraining = trainingService.IsTraining
                trainingService.Pause ()
                this.UpdateUI ()
                this.GeneratePreviewMesh (fun () ->
                    if wasTraining then
                        trainingService.Run ()
                        this.BeginInvokeOnMainThread (fun _ -> this.UpdateUI ())))
        |]

    member this.UpdatePointCloud () =
        SCNTransaction.Begin ()
        for fi in project.DepthPaths do
            let node = framePointNodes.GetOrAdd (fi, fun fi ->
                let f = project.GetFrame fi
                let n = f.CreatePointNode (UIColor.SystemOrange)
                n.Opacity <- nfloat 0.05
                pointCloudNode.AddChildNode n
                n)
            ()
        SCNTransaction.Commit ()

    member private this.GeneratePreviewMesh (k : unit -> unit) =
        let setProgress p =
            this.BeginInvokeOnMainThread (fun _ ->
                if 1e-6f <= p && p < 0.995f then
                    previewButton.Enabled <- false
                    previewProgress.Progress <- p
                    previewProgress.Alpha <- nfloat 1.0
                else
                    previewButton.Enabled <- true
                    previewProgress.Alpha <- nfloat 0.0)
        Threading.ThreadPool.QueueUserWorkItem (fun _ ->
            setProgress 0.0f
            try
                let mesh = trainingService.GenerateMesh (32, setProgress)
                let node =
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
                SCNTransaction.Begin ()
                match previewNeuralMeshNode with
                | None -> ()
                | Some x -> x.RemoveFromParentNode ()
                previewNeuralMeshNode <- Some node
                rootNode.AddChildNode node
                SCNTransaction.Commit ()
            with ex ->
                this.ShowError ex
            setProgress 1.1f
            k ())
        |> ignore

