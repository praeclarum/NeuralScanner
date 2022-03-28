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
    let lossView = new LossGraphView ()
    do lossView.SetLosses (trainingService.Losses)

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
            capturePanel.LayoutCenterX == lossView.LayoutCenterX
            capturePanel.LayoutBottom == trainButtons.LayoutTop - 22.0
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

            nameField.EditingChanged.Subscribe (fun _ ->
                project.Name <- nameField.Text)

            captureButton.TouchUpInside.Subscribe(fun _ -> this.HandleCapture())
            trainButton.TouchUpInside.Subscribe (fun _ ->
                trainingService.Run ()
                this.UpdateUI ())
            pauseTrainButton.TouchUpInside.Subscribe (fun _ ->
                trainingService.Pause ()
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



