namespace NeuralScanner


open System
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
                                 BackgroundColor = UIColor.SystemRed)
    let lossView = new LossGraphView ()
    do lossView.SetLosses (trainingService.Losses)

    let captureButton = UIButton.FromType(UIButtonType.RoundedRect)
    do
        captureButton.SetTitle("Scan Object", UIControlState.Normal)
        captureButton.SetImage(UIImage.GetSystemImage "viewfinder", UIControlState.Normal)

    let trainButton = UIButton.FromType(UIButtonType.RoundedRect)
    let pauseTrainButton = UIButton.FromType(UIButtonType.RoundedRect)
    do
        trainButton.SetTitle("Train", UIControlState.Normal)
        trainButton.TouchUpInside.Add (fun _ -> trainingService.Run ())
        pauseTrainButton.SetTitle("Pause Training", UIControlState.Normal)
        pauseTrainButton.TouchUpInside.Add (fun _ -> trainingService.Pause ())
    let trainButtons = new UIStackView (Axis = UILayoutConstraintAxis.Horizontal)
    do trainButtons.AddArrangedSubview (trainButton)
    do trainButtons.AddArrangedSubview (pauseTrainButton)
    do trainButtons.Spacing <- nfloat 44.0

    let nameField = new UITextField (Placeholder = "Name", Text = project.Name)

    let labelCaptureInfo = new UILabel (Text = "Object not scanned")

    let stackView = new UIStackView (Axis = UILayoutConstraintAxis.Vertical)

    member this.HandleCapture () =
        let captureVC = new CaptureViewController (project)
        let captureNC = new UINavigationController (captureVC)
        captureNC.ModalPresentationStyle <- UIModalPresentationStyle.PageSheet
        this.PresentViewController(captureNC, true, null)
        ()

    override this.ViewDidLoad () =
        base.ViewDidLoad ()

    override this.AddUI view =
        //
        // Layout
        //
        stackView.Frame <- this.View.Bounds
        stackView.Alignment <- UIStackViewAlignment.Center
        stackView.Distribution <- UIStackViewDistribution.EqualSpacing

        stackView.TranslatesAutoresizingMaskIntoConstraints <- false
        stackView.AddArrangedSubview (nameField)
        stackView.AddArrangedSubview (labelCaptureInfo)
        stackView.AddArrangedSubview (captureButton)
        //stackView.AddArrangedSubview (lossView)
        stackView.AddArrangedSubview (trainButtons)
        stackView.AddArrangedSubview (new UIView ())

        sceneView.Frame <- this.View.Bounds
        sceneView.AutoresizingMask <- UIViewAutoresizing.FlexibleDimensions

        lossView.TranslatesAutoresizingMaskIntoConstraints <- false

        let view = this.View
        view.AddSubview (sceneView)
        view.AddSubview (stackView)
        view.AddSubview (lossView)

        [|
            view.SafeAreaLayoutGuide.LayoutTop == stackView.LayoutTop
            view.SafeAreaLayoutGuide.LayoutLeft == stackView.LayoutLeft
            view.SafeAreaLayoutGuide.LayoutRight == stackView.LayoutRight
            view.SafeAreaLayoutGuide.LayoutBottom == stackView.LayoutBottom
            lossView.LayoutLeft == view.LayoutLeft
            lossView.LayoutRight == view.LayoutRight
            lossView.LayoutBottom == view.LayoutBottom
            lossView.LayoutTop == view.SafeAreaLayoutGuide.LayoutBottom - 128.0
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
            trainButton.Enabled <- true
            pauseTrainButton.Enabled <- false
        ()

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





