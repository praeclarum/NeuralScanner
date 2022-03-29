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

    let learningRateSlider = new UISlider(MinValue = 0.0f, MaxValue = 1.0f,
                                          TranslatesAutoresizingMaskIntoConstraints = false)
    let mutable updatingSlider = false
    let sliderToLearningRate (v : float32) =
        MathF.Pow (10.0f, -((1.0f-v)*6.0f + 1.0f))
    let learningRateToSlider (lr : float32) =
        1.0f - ((-MathF.Log10 (lr)-1.0f) / 6.0f)
    let learningRateLabel = new UILabel(Alpha = nfloat 0.75, TranslatesAutoresizingMaskIntoConstraints = false)

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
    do
        trainButton.SetTitle("Train", UIControlState.Normal)
        trainButton.TouchUpInside.Add (fun _ -> trainingService.Run ())
        pauseTrainButton.SetTitle("Pause Training", UIControlState.Normal)
        pauseTrainButton.TouchUpInside.Add (fun _ -> trainingService.Pause ())
    let trainButtons = new UIStackView (Axis = UILayoutConstraintAxis.Horizontal, Spacing = nfloat 44.0)
    do trainButtons.AddArrangedSubview trainButton
    do trainButtons.AddArrangedSubview pauseTrainButton
    let nameField = new UITextField (Placeholder = "Name", Text = project.Name)

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
        view.AddSubview learningRateLabel

        [|
            nameField.LayoutTop == view.SafeAreaLayoutGuide.LayoutTop
            nameField.LayoutLeading == view.SafeAreaLayoutGuide.LayoutLeading
            nameField.LayoutTrailing == view.SafeAreaLayoutGuide.LayoutTrailing
            lossView.LayoutLeft == view.LayoutLeft
            lossView.LayoutRight == view.LayoutRight
            lossView.LayoutBottom == view.LayoutBottom
            lossView.LayoutTop == view.SafeAreaLayoutGuide.LayoutBottom - 128.0
            trainButtons.LayoutCenterX == lossView.LayoutCenterX
            trainButtons.LayoutBottom == lossView.LayoutTop
            learningRateSlider.LayoutCenterX == lossView.LayoutCenterX
            learningRateSlider.LayoutBottom == trainButtons.LayoutTop
            learningRateSlider.LayoutWidth == lossView.LayoutWidth * 0.5
            learningRateSlider.LayoutHeight == 44.0
            learningRateLabel.LayoutLeading == learningRateSlider.LayoutTrailing + 11.0f
            learningRateLabel.LayoutCenterY == learningRateSlider.LayoutCenterY
            capturePanel.LayoutCenterX == lossView.LayoutCenterX
            capturePanel.LayoutBottom == learningRateSlider.LayoutTop - 22.0
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
        learningRateLabel.Text <- sprintf "%0.6f" project.Settings.LearningRate
        if not updatingSlider then
            learningRateSlider.Value <- learningRateToSlider project.Settings.LearningRate
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

            learningRateSlider.ValueChanged.Subscribe (fun _ ->
                updatingSlider <- true
                let lr = sliderToLearningRate learningRateSlider.Value
                project.Settings.LearningRate <- lr
                this.UpdateUI ()
                updatingSlider <- false
                ())

            nameField.EditingChanged.Subscribe (fun _ ->
                project.Name <- nameField.Text)

            captureButton.TouchUpInside.Subscribe(fun _ -> this.HandleCapture())
            trainButton.TouchUpInside.Subscribe (fun _ ->
                trainingService.Run ()
                this.UpdateUI ())
            pauseTrainButton.TouchUpInside.Subscribe (fun _ ->
                trainingService.Pause ()
                this.GeneratePreviewMesh ()
                this.UpdateUI ())
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

    member this.GeneratePreviewMesh () =
        Threading.ThreadPool.QueueUserWorkItem (fun _ ->
            let mesh = trainingService.GenerateMesh ()
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
            let node = SCNNode.FromGeometry(geometry)
            SCNTransaction.Begin ()
            match previewNeuralMeshNode with
            | None -> ()
            | Some x -> x.RemoveFromParentNode ()
            previewNeuralMeshNode <- Some node
            rootNode.AddChildNode node
            SCNTransaction.Commit ()
            ())
        |> ignore

