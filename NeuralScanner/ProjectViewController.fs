namespace NeuralScanner


open System
open Foundation
open UIKit

open Praeclarum.AutoLayout


type ProjectViewController (project : Project) =
    inherit UIViewController (Title = "Project")

    let trainingService = TrainingServices.getForProject (project)
    let lossView = new LossGraphView ()
    do lossView.SetLosses (trainingService.Losses)

    let captureButton = UIButton.FromType(UIButtonType.RoundedRect)
    do
        captureButton.SetTitle("Scan Object", UIControlState.Normal)

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

    let stackView = new UIStackView ()
    do stackView.Axis <- UILayoutConstraintAxis.Vertical

    member this.HandleCapture () =
        let captureVC = new CaptureViewController (project)
        let captureNC = new UINavigationController (captureVC)
        captureNC.ModalPresentationStyle <- UIModalPresentationStyle.PageSheet
        this.PresentViewController(captureNC, true, null)
        ()

    member this.UpdateUI () =
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

    override this.ViewDidLoad () =
        base.ViewDidLoad ()

        project.Changed.Add (fun _ ->
            this.BeginInvokeOnMainThread (fun _ ->
                this.UpdateUI ()))
        trainingService.Changed.Add (fun _ ->
            this.BeginInvokeOnMainThread (fun _ ->
                this.UpdateUI ()))
        trainingService.BatchTrained.Add (fun (progress, totalTrained, loss) ->
            this.BeginInvokeOnMainThread (fun _ ->
                lossView.AddLoss (progress, loss)))
        this.UpdateUI ()

        nameField.ValueChanged.Add (fun _ ->
            project.Name <- nameField.Text)
        nameField.EditingChanged.Add (fun _ ->
            project.Name <- nameField.Text)

        captureButton.TouchUpInside.Add(fun _ -> this.HandleCapture())
        trainButton.TouchUpInside.Add (fun _ ->
            trainingService.Run ()
            this.UpdateUI ())
        pauseTrainButton.TouchUpInside.Add (fun _ ->
            trainingService.Pause ()
            this.UpdateUI ())

        this.View.BackgroundColor <- UIColor.SystemBackground

        stackView.Frame <- this.View.Bounds
        stackView.AutoresizingMask <- UIViewAutoresizing.FlexibleDimensions
        stackView.Alignment <- UIStackViewAlignment.Center
        stackView.Distribution <- UIStackViewDistribution.EqualSpacing

        stackView.TranslatesAutoresizingMaskIntoConstraints <- false
        stackView.AddArrangedSubview (nameField)
        stackView.AddArrangedSubview (labelCaptureInfo)
        stackView.AddArrangedSubview (captureButton)
        stackView.AddArrangedSubview (lossView)
        stackView.AddArrangedSubview (trainButtons)
        stackView.AddArrangedSubview (new UIView ())

        this.View.AddSubview (stackView)
        this.View.AddConstraints
            [|
                this.View.SafeAreaLayoutGuide.LayoutTop == stackView.LayoutTop
                this.View.SafeAreaLayoutGuide.LayoutBottom == stackView.LayoutBottom
                this.View.SafeAreaLayoutGuide.LayoutLeft == stackView.LayoutLeft
                this.View.SafeAreaLayoutGuide.LayoutRight == stackView.LayoutRight
            |]




