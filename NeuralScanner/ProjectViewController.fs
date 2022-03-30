﻿namespace NeuralScanner


open System
open System.Collections.Concurrent

open Foundation
open UIKit
open SceneKit

open Praeclarum.AutoLayout

[<Flags>]
type ViewObjectType =
    | DepthPoints = 1
    | SolidVoxels = 2
    | SolidMesh = 4

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
    let learningRateSlider = new ValueSlider ("Learning Rate", "0.0000000",
                                              0.0f, 1.0f,
                                              sliderToLearningRate,
                                              learningRateToSlider)

    let previewResolutionSlider = new ValueSlider ("Resolution", "0",
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

    let viewBoundsButton = new ToggleButton ("Bounds")
    let viewPointsButton = new ToggleButton ("Points")
    let viewVoxelsButton = new ToggleButton ("Voxels")
    let viewSolidMeshButton = new ToggleButton ("Solid Mesh")
    let viewButtons = new UIStackView (Axis = UILayoutConstraintAxis.Vertical, Spacing = nfloat 11.0, TranslatesAutoresizingMaskIntoConstraints = false)
    do
        viewButtons.AddArrangedSubview viewBoundsButton
        viewButtons.AddArrangedSubview viewPointsButton
        viewButtons.AddArrangedSubview viewVoxelsButton
        viewButtons.AddArrangedSubview viewSolidMeshButton

    let scene = SCNScene.Create()
    do sceneView.Scene <- scene
    let rootNode = scene.RootNode
    let pointCloudNode = new SCNNode (Name = "PointCloud")
    do rootNode.AddChildNode pointCloudNode
    let framePointNodes = ConcurrentDictionary<string, SCNNode> ()
    let mutable solidMeshNode : SCNNode option = None
    let mutable solidVoxelsNode : SCNNode option = None
    let boundsNode = new SCNNode (Name = "Bounds")
    do rootNode.AddChildNode boundsNode

    let mutable visibleTypes : ViewObjectType = ViewObjectType.DepthPoints ||| ViewObjectType.SolidVoxels ||| ViewObjectType.SolidMesh

    member this.HandleCapture () =
        let captureVC = new CaptureViewController (project)
        let captureNC = new UINavigationController (captureVC)
        captureNC.ModalPresentationStyle <- UIModalPresentationStyle.PageSheet
        this.PresentViewController(captureNC, true, null)
        ()

    override this.ViewDidLoad () =
        base.ViewDidLoad ()
        this.NavigationItem.RightBarButtonItems <-
            [|
                new UIBarButtonItem (UIImage.GetSystemImage "gearshape", UIBarButtonItemStyle.Plain, this, new ObjCRuntime.Selector ("showProjectSettings:"))
            |]

    member this.SettingsButton = this.NavigationItem.RightBarButtonItems.[0]

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
        view.AddSubview viewButtons

        let viewButtonGap = 11.0

        [|
            nameField.LayoutTop == view.SafeAreaLayoutGuide.LayoutTop
            nameField.LayoutLeading == view.SafeAreaLayoutGuide.LayoutLeading
            nameField.LayoutTrailing == view.SafeAreaLayoutGuide.LayoutTrailing
            previewResolutionSlider.LayoutTop == nameField.LayoutBottom + 11
            previewResolutionSlider.LayoutCenterX == nameField.LayoutCenterX
            previewResolutionSlider.LayoutWidth == view.LayoutWidth * 0.5

            lossView.LayoutLeft == view.LayoutLeft
            lossView.LayoutRight == view.LayoutRight
            lossView.LayoutBottom == view.LayoutBottom
            lossView.LayoutTop == view.SafeAreaLayoutGuide.LayoutBottom - 88.0
            trainButtons.LayoutCenterX == lossView.LayoutCenterX
            trainButtons.LayoutBottom == lossView.LayoutTop
            learningRateSlider.LayoutCenterX == lossView.LayoutCenterX
            learningRateSlider.LayoutBottom == trainButtons.LayoutTop
            learningRateSlider.LayoutWidth == lossView.LayoutWidth * 0.5

            capturePanel.LayoutBaseline == nameField.LayoutBaseline
            capturePanel.LayoutRight == view.SafeAreaLayoutGuide.LayoutRight - 11
            viewButtons.LayoutTop == capturePanel.LayoutBottom + 11
            viewButtons.LayoutRight == view.SafeAreaLayoutGuide.LayoutRight - 11
            previewButton.LayoutTop == viewButtons.LayoutBottom + 33
            previewButton.LayoutCenterX == viewButtons.LayoutCenterX
            previewProgress.LayoutTop == previewButton.LayoutBottom
            previewProgress.LayoutHeight == 4
            previewProgress.LayoutLeft == viewButtons.LayoutLeft
            previewProgress.LayoutRight == viewButtons.LayoutRight
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
        if not previewResolutionSlider.UserInteracting then
            previewResolutionSlider.Value <- project.Settings.Resolution

        if visibleTypes.HasFlag (ViewObjectType.DepthPoints) then
            pointCloudNode.Hidden <- false
            viewPointsButton.Selected <- true
        else
            pointCloudNode.Hidden <- true
            viewPointsButton.Selected <- false
        viewPointsButton.Enabled <- project.NumCaptures > 0

        if visibleTypes.HasFlag (ViewObjectType.SolidVoxels) then
            viewVoxelsButton.Selected <- true
            match solidVoxelsNode with
            | None -> viewVoxelsButton.Enabled <- false
            | Some x -> x.Hidden <- false; viewVoxelsButton.Enabled <- true
        else
            viewVoxelsButton.Selected <- false
            match solidVoxelsNode with
            | None -> viewVoxelsButton.Enabled <- false
            | Some x -> x.Hidden <- true; viewVoxelsButton.Enabled <- true
        if visibleTypes.HasFlag (ViewObjectType.SolidMesh) then
            viewSolidMeshButton.Selected <- true
            match solidMeshNode with
            | None -> viewSolidMeshButton.Enabled <- false
            | Some x -> x.Hidden <- false; viewSolidMeshButton.Enabled <- true
        else
            viewSolidMeshButton.Selected <- false
            match solidMeshNode with
            | None -> viewSolidMeshButton.Enabled <- false
            | Some x -> x.Hidden <- true; viewSolidMeshButton.Enabled <- true

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
            trainingService.BatchTrained.Subscribe (fun (batchSize, totalTrained, loss) ->
                this.BeginInvokeOnMainThread (fun _ ->
                    lossView.AddLoss (batchSize, totalTrained, loss)))

            learningRateSlider.ValueChanged.Subscribe (fun lr ->
                project.Settings.LearningRate <- lr
                project.SetModified "Settings.LearningRate")
            previewResolutionSlider.ValueChanged.Subscribe (fun r ->
                project.Settings.Resolution <- r
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
                previewButton.Enabled <- false
                let wasTraining = trainingService.IsTraining
                trainingService.Pause ()
                this.UpdateUI ()
                this.GeneratePreviewMesh (fun () ->
                    if wasTraining then
                        trainingService.Run ()
                    this.BeginInvokeOnMainThread (fun _ ->
                        previewButton.Enabled <- true
                        this.UpdateUI ())))

            viewPointsButton.TouchUpInside.Subscribe (fun _ -> this.ToggleVisible (ViewObjectType.DepthPoints))
            viewVoxelsButton.TouchUpInside.Subscribe (fun _ -> this.ToggleVisible (ViewObjectType.SolidVoxels))
            viewSolidMeshButton.TouchUpInside.Subscribe (fun _ -> this.ToggleVisible (ViewObjectType.SolidMesh))
        |]

    [<Export("showProjectSettings:")>]
    member this.ShowProjectSettings (sender : NSObject) =
        let vc = new ProjectSettingsViewController (project)
        this.PresentPopover (vc, this.SettingsButton)

    member this.ToggleVisible (o : ViewObjectType) =
        if visibleTypes.HasFlag o then
            this.SetVisible (visibleTypes &&& ~~~o)
        else
            this.SetVisible (visibleTypes ||| o)

    member this.SetVisible (os : ViewObjectType) =
        visibleTypes <- os
        this.UpdateUI ()

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
                    previewProgress.Progress <- p
                    previewProgress.Alpha <- nfloat 1.0
                else
                    previewProgress.Alpha <- nfloat 0.0)
        Threading.ThreadPool.QueueUserWorkItem (fun _ ->
            setProgress 0.0f
            try
                let voxels = trainingService.GenerateVoxels (setProgress)
                let vnode = this.CreateSolidVoxelsNode voxels
                vnode.Hidden <- not (visibleTypes.HasFlag (ViewObjectType.SolidVoxels))
                let mesh = trainingService.GenerateMesh voxels
                let node = this.CreateSolidMeshNode mesh
                node.Hidden <- not (visibleTypes.HasFlag (ViewObjectType.SolidMesh))
                SCNTransaction.Begin ()
                match solidVoxelsNode with
                | None -> ()
                | Some x -> x.RemoveFromParentNode ()
                solidVoxelsNode <- Some vnode
                rootNode.AddChildNode vnode
                match solidMeshNode with
                | None -> ()
                | Some x -> x.RemoveFromParentNode ()
                solidMeshNode <- Some node
                rootNode.AddChildNode node
                SCNTransaction.Commit ()
            with ex ->
                this.ShowError ex
            setProgress 1.1f
            k ())
        |> ignore

    member private this.CreateSolidVoxelsNode (voxels : SdfKit.Voxels) : SCNNode =
        let nx = voxels.NX
        let ny = voxels.NY
        let nz = voxels.NZ
        let m = voxels.Min
        let dx = (voxels.Max.X - m.X) / float32 (nx - 1)
        let dy = (voxels.Max.Y - m.Y) / float32 (ny - 1)
        let dz = (voxels.Max.Z - m.Z) / float32 (nz - 1)
        let n = nx * ny * nz
        let vs = voxels.Values
        if n > 0 then
            let verts = ResizeArray<SCNVector3> ()
            for ix in 0..(nx - 1) do
                let x = m.X + dx * float32 ix
                for iy in 0..(ny - 1) do
                    let y = m.Y + dy * float32 iy
                    for iz in 0..(nz - 1) do
                        let v = vs.[ix, iy, iz]
                        if v <= 0.0f then
                            let z = m.Z + dz * float32 iz
                            verts.Add (SCNVector3 (x, y, z))
            let verts = verts.ToArray ()
            let vertsSource =
                verts
                |> SCNGeometrySource.FromVertices
            let element =
                let elemStream = new IO.MemoryStream ()
                let elemWriter = new IO.BinaryWriter (elemStream)
                for i in 0..(verts.Length - 1) do
                    elemWriter.Write (i)
                elemWriter.Flush ()
                elemStream.Position <- 0L
                let data = NSData.FromStream (elemStream)
                SCNGeometryElement.FromData (data, SCNGeometryPrimitiveType.Point, nint verts.Length, nint 4)
            element.PointSize <- nfloat dx
            element.MinimumPointScreenSpaceRadius <- nfloat 1.0
            element.MaximumPointScreenSpaceRadius <- nfloat 10.0
            let geometry = SCNGeometry.Create([|vertsSource|], [|element|])
            let material = SCNMaterial.Create ()
            material.Emission.ContentColor <- UIColor.Green
            material.ReadsFromDepthBuffer <- true
            material.WritesToDepthBuffer <- true
            material.Transparency <- nfloat 0.125
            geometry.FirstMaterial <- material
            let node = SCNNode.FromGeometry(geometry)
            node
        else
            SCNNode.Create ()

    member private this.CreateSolidMeshNode (mesh : SdfKit.Mesh) : SCNNode =
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
