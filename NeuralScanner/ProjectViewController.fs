namespace NeuralScanner


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
    | Bounds = 8
    | InsidePoints = 16
    | OutsidePoints = 32
    | FreespacePoints = 64
    | RoughMesh = 128

type ProjectViewController (project : Project) =
    inherit BaseViewController ()

    // State
    let mutable viewingMeshPath : string option = None
    let mutable touchState = NotTouching

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
                                                   16.0f, 256.0f,
                                                   (fun x -> MathF.Round x),
                                                   (fun x -> x))
    let exportMeshButton = UIButton.FromType(UIButtonType.RoundedRect)
    do
        exportMeshButton.TranslatesAutoresizingMaskIntoConstraints <- false
        exportMeshButton.SetImage(UIImage.GetSystemImage "square.and.arrow.up", UIControlState.Normal)
    let arMeshButton = UIButton.FromType(UIButtonType.RoundedRect)
    do
        arMeshButton.TranslatesAutoresizingMaskIntoConstraints <- false
        arMeshButton.SetImage(UIImage.GetSystemImage "eye.fill", UIControlState.Normal)
    let previewButton = UIButton.FromType(UIButtonType.RoundedRect)
    do
        previewButton.TranslatesAutoresizingMaskIntoConstraints <- false
        previewButton.SetTitle("Generate", UIControlState.Normal)
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
    let trainButtons = new UIStackView (Axis = UILayoutConstraintAxis.Horizontal, Distribution = UIStackViewDistribution.FillProportionally)
    do trainButtons.AddArrangedSubview trainButton
    do trainButtons.AddArrangedSubview pauseTrainButton
    do trainButtons.AddArrangedSubview resetTrainButton
    let nameField = new UITextField (Font = UIFont.PreferredTitle1, Placeholder = "Name", Text = project.Name)

    let previewProgress = new UIProgressView (Alpha = nfloat 0.0f, TranslatesAutoresizingMaskIntoConstraints = false)

    let scansButton =
        let b = UIButton.FromType (UIButtonType.RoundedRect)
        b.TranslatesAutoresizingMaskIntoConstraints <- false
        b.SetImage (UIImage.GetSystemImage ("eye.fill"), UIControlState.Normal)
        b

    let viewBoundsButton = new ToggleButton ("Bounds")
    let viewPointsButton = new ToggleButton ("Points")
    let viewInsidePointsButton = new ToggleButton ("Inside")
    let viewOutsidePointsButton = new ToggleButton ("Outside")
    let viewFreespacePointsButton = new ToggleButton ("Freespace")
    let viewVoxelsButton = new ToggleButton ("Voxels")
    let viewSolidMeshButton = new ToggleButton ("Solid Mesh")
    let viewRoughMeshButton = new ToggleButton ("Rough Mesh")
    let viewButtons = new UIStackView (Axis = UILayoutConstraintAxis.Vertical, Spacing = nfloat 11.0, TranslatesAutoresizingMaskIntoConstraints = false)
    do
        viewButtons.AddArrangedSubview viewBoundsButton
        viewButtons.AddArrangedSubview viewPointsButton
        viewButtons.AddArrangedSubview viewRoughMeshButton
        //viewButtons.AddArrangedSubview viewInsidePointsButton
        //viewButtons.AddArrangedSubview viewOutsidePointsButton
        //viewButtons.AddArrangedSubview viewFreespacePointsButton
        //viewButtons.AddArrangedSubview viewVoxelsButton
        viewButtons.AddArrangedSubview viewSolidMeshButton

    let scene = SCNScene.Create()
    do sceneView.Scene <- scene
    let rootNode = scene.RootNode
    //let cam = SCNCamera.Create()
    //do
    //    cam.FieldOfView <- nfloat 60.0
    //    cam.ZNear <- 0.001
    //    cam.ZFar <- 100.0
    //let camNode = SCNNode.Create ()
    //do
    //    camNode.Camera <- cam
    //    let mutable t =  SCNMatrix4.LookAt (1.0f, 2.0f, 3.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f)
    //    t.Invert ()
    //    camNode.Transform <- t
    //    rootNode.AddChildNode camNode
    //    sceneView.PointOfView <- camNode

    let boundsPos =
        [|
            SCNVector3 (+1.0f, +1.0f, +1.0f)
            SCNVector3 (+1.0f, +1.0f, -1.0f)
            SCNVector3 (+1.0f, -1.0f, +1.0f)
            SCNVector3 (+1.0f, -1.0f, -1.0f)
            SCNVector3 (-1.0f, +1.0f, +1.0f)
            SCNVector3 (-1.0f, +1.0f, -1.0f)
            SCNVector3 (-1.0f, -1.0f, +1.0f)
            SCNVector3 (-1.0f, -1.0f, -1.0f)
        |]
    let boundsHandles =
        Array.init 8 (fun i ->
            let g = SCNBox.Create (nfloat 0.2, nfloat 0.2, nfloat 0.2, nfloat 0.0)
            let m = g.FirstMaterial
            m.Diffuse.ContentColor <- UIColor.SystemGray
            let n = SCNNode.FromGeometry g
            n.Position <- boundsPos.[i]
            n.Name <- sprintf "BoundsHandle%d" i
            n)
    let boundsNode =
        let g = SCNBox.Create (nfloat 2.0, nfloat 2.0, nfloat 2.0, nfloat 0.0)
        let m = g.FirstMaterial
        m.Transparency <- nfloat 0.25
        m.Diffuse.ContentColor <- UIColor.SystemGray
        let n = SCNNode.FromGeometry g
        n.Name <- "Bounds"
        n.AddNodes boundsHandles
        n
    do rootNode.AddChildNode boundsNode

    let pointCloudNode = new SCNNode (Name = "PointCloud")
    do rootNode.AddChildNode pointCloudNode
    let insidePointsNode = new SCNNode (Name = "InsidePoints")
    let outsidePointsNode = new SCNNode (Name = "OutsidePoints")
    let freespacePointsNode = new SCNNode (Name = "FreespacePoints")
    do rootNode.AddNodes [| insidePointsNode; outsidePointsNode; freespacePointsNode |]

    let framePointNodes = ConcurrentDictionary<string, SCNNode> ()
    let mutable roughMeshNode : SCNNode option = None
    let mutable solidMeshNode : SCNNode option = None
    let mutable solidVoxelsNode : SCNNode option = None

    let mutable visibleTypes : ViewObjectType =
        ViewObjectType.DepthPoints
        //||| ViewObjectType.Bounds
        //||| ViewObjectType.SolidVoxels
        //||| ViewObjectType.RoughMesh
        ||| ViewObjectType.SolidMesh
        ||| ViewObjectType.InsidePoints
        ||| ViewObjectType.OutsidePoints
        ||| ViewObjectType.FreespacePoints

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
        if project.NumCaptures > 0 then
            Threading.ThreadPool.QueueUserWorkItem (fun _ ->
                this.GenerateRoughMesh (fun () ->
                    this.BeginInvokeOnMainThread (fun _ ->
                        this.UpdateUI ())))
            |> ignore

    member this.SettingsButton = this.NavigationItem.RightBarButtonItems.[0]

    override this.AddUI view =
        capturePanel.TranslatesAutoresizingMaskIntoConstraints <- false

        sceneView.Frame <- this.View.Bounds
        sceneView.AutoresizingMask <- UIViewAutoresizing.FlexibleDimensions
        sceneView.AllowsCameraControl <- false

        lossView.TranslatesAutoresizingMaskIntoConstraints <- false

        nameField.TextAlignment <- UITextAlignment.Center
        nameField.TranslatesAutoresizingMaskIntoConstraints <- false

        trainButtons.TranslatesAutoresizingMaskIntoConstraints <- false

        view.AddSubview (sceneView)
        view.AddSubview (lossView)
        view.AddSubview nameField
        view.AddSubview trainButtons
        view.AddSubview capturePanel
        //view.AddSubview learningRateSlider
        view.AddSubview previewProgress
        view.AddSubview previewButton
        view.AddSubview previewResolutionSlider
        view.AddSubview viewButtons
        view.AddSubview scansButton
        view.AddSubview exportMeshButton
        view.AddSubview arMeshButton

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
            trainButtons.LayoutLeft == view.SafeAreaLayoutGuide.LayoutLeft
            trainButtons.LayoutRight == view.SafeAreaLayoutGuide.LayoutRight
            trainButtons.LayoutBottom == lossView.LayoutTop
            //learningRateSlider.LayoutCenterX == lossView.LayoutCenterX
            //learningRateSlider.LayoutBottom == trainButtons.LayoutTop
            //learningRateSlider.LayoutWidth == lossView.LayoutWidth * 0.5

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

            scansButton.LayoutRight == viewButtons.LayoutLeft - 11
            scansButton.LayoutBaseline == viewPointsButton.LayoutBaseline
            exportMeshButton.LayoutCenterX == scansButton.LayoutCenterX
            exportMeshButton.LayoutBaseline == viewSolidMeshButton.LayoutBaseline
            arMeshButton.LayoutRight == exportMeshButton.LayoutLeft - 11
            arMeshButton.LayoutBaseline == viewSolidMeshButton.LayoutBaseline
        |]

    override this.UpdateUI () =
        labelCaptureInfo.Text <-
            if project.NumCaptures = 0 then "Object not scanned"
            else sprintf "%d depth scans" project.NumCaptures
        captureButton.Hidden <- project.NumCaptures > 0
        if trainingService.IsTraining then
            trainButton.Enabled <- false
            resetTrainButton.Enabled <- false
            pauseTrainButton.Enabled <- true
        else
            trainButton.Enabled <- project.NumCaptures > 0
            pauseTrainButton.Enabled <- false
            resetTrainButton.Enabled <- true
        if not learningRateSlider.UserInteracting then
            learningRateSlider.Value <- project.Settings.LearningRate
        if not previewResolutionSlider.UserInteracting then
            previewResolutionSlider.Value <- project.Settings.Resolution

        if visibleTypes.HasFlag (ViewObjectType.Bounds) then
            let t = project.ClipTransform
            boundsNode.Transform <- t
            boundsNode.Hidden <- false
            viewBoundsButton.Selected <- true
            sceneView.AllowsCameraControl <- false
        else
            boundsNode.Hidden <- true
            viewBoundsButton.Selected <- false
            sceneView.AllowsCameraControl <- true

        if visibleTypes.HasFlag (ViewObjectType.DepthPoints) then
            pointCloudNode.Hidden <- false
            viewPointsButton.Selected <- true
        else
            pointCloudNode.Hidden <- true
            viewPointsButton.Selected <- false
        viewPointsButton.Enabled <- project.NumCaptures > 0
        scansButton.Enabled <- project.NumCaptures > 0

        if visibleTypes.HasFlag (ViewObjectType.InsidePoints) then
            insidePointsNode.Hidden <- false
            viewInsidePointsButton.Selected <- true
        else
            insidePointsNode.Hidden <- true
            viewInsidePointsButton.Selected <- false
        if visibleTypes.HasFlag (ViewObjectType.OutsidePoints) then
            outsidePointsNode.Hidden <- false
            viewOutsidePointsButton.Selected <- true
        else
            outsidePointsNode.Hidden <- true
            viewOutsidePointsButton.Selected <- false
        if visibleTypes.HasFlag (ViewObjectType.FreespacePoints) then
            freespacePointsNode.Hidden <- false
            viewFreespacePointsButton.Selected <- true
        else
            freespacePointsNode.Hidden <- true
            viewFreespacePointsButton.Selected <- false

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
        exportMeshButton.Enabled <- viewingMeshPath.IsSome
        arMeshButton.Enabled <- viewingMeshPath.IsSome

        if visibleTypes.HasFlag (ViewObjectType.RoughMesh) then
            viewRoughMeshButton.Selected <- true
            match roughMeshNode with
            | None -> viewRoughMeshButton.Enabled <- false
            | Some x -> x.Hidden <- false; viewRoughMeshButton.Enabled <- true
        else
            viewRoughMeshButton.Selected <- false
            match roughMeshNode with
            | None -> viewRoughMeshButton.Enabled <- false
            | Some x -> x.Hidden <- true; viewRoughMeshButton.Enabled <- true

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
            trainingService.Error.Subscribe this.ShowError
            trainingService.BatchTrained.Subscribe (fun (batchSize, numPointsPerEpoch, loss, batchData) ->
                this.HandleBatchData batchData
                this.BeginInvokeOnMainThread (fun _ ->
                    lossView.AddLoss (batchSize, project.Settings.TotalTrainedPoints, project.Settings.TotalTrainedSeconds, numPointsPerEpoch, loss)))

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
                trainButton.Enabled <- false
                resetTrainButton.Enabled <- false
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
                this.GenerateSolidMesh (fun () ->
                    if wasTraining then
                        trainingService.Run ()
                    this.BeginInvokeOnMainThread (fun _ ->
                        previewButton.Enabled <- true
                        this.UpdateUI ())))

            scansButton.TouchUpInside.Subscribe (fun _ -> this.ShowFrames ())
            exportMeshButton.TouchUpInside.Subscribe (fun _ -> this.ExportSolidMesh ())
            arMeshButton.TouchUpInside.Subscribe (fun _ -> this.ARSolidMesh ())
            viewPointsButton.TouchUpInside.Subscribe (fun _ -> this.ToggleVisible (ViewObjectType.DepthPoints))
            viewInsidePointsButton.TouchUpInside.Subscribe (fun _ -> this.ToggleVisible (ViewObjectType.InsidePoints))
            viewOutsidePointsButton.TouchUpInside.Subscribe (fun _ -> this.ToggleVisible (ViewObjectType.OutsidePoints))
            viewFreespacePointsButton.TouchUpInside.Subscribe (fun _ -> this.ToggleVisible (ViewObjectType.FreespacePoints))
            viewVoxelsButton.TouchUpInside.Subscribe (fun _ -> this.ToggleVisible (ViewObjectType.SolidVoxels))
            viewRoughMeshButton.TouchUpInside.Subscribe (fun _ -> this.ToggleVisible (ViewObjectType.RoughMesh))
            viewSolidMeshButton.TouchUpInside.Subscribe (fun _ -> this.ToggleVisible (ViewObjectType.SolidMesh))
            viewBoundsButton.TouchUpInside.Subscribe (fun _ -> this.ToggleVisible (ViewObjectType.Bounds))
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
        Threading.Tasks.Parallel.ForEach (project.DepthPaths, fun fi ->
            let f = project.GetFrame fi
            SCNTransaction.Begin ()
            SCNTransaction.AnimationDuration <- 1.0
            if f.Visible then
                let node = framePointNodes.GetOrAdd (fi, fun fi ->
                    let n = f.CreatePointNode (null)
                    n.Opacity <- nfloat 0.05
                    pointCloudNode.AddChildNode n
                    n)
                node.WorldTransform <- SceneKitGeometry.matrixToSCNMatrix4 f.CameraToWorldTransform
                node.Hidden <- false
            else
                match framePointNodes.TryGetValue fi with
                | true, n -> n.Hidden <- true
                | _ -> ()
            SCNTransaction.Commit ())
        |> ignore

    member private this.GenerateRoughMesh (k : unit -> unit) =
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
                let voxels = Meshes.generateRoughVoxels trainingService.Data setProgress
                let mesh = Meshes.meshFromVoxels voxels
                let node = SceneKitGeometry.createSolidMeshNode mesh
                node.Hidden <- not (visibleTypes.HasFlag (ViewObjectType.RoughMesh))
                node.Opacity <- nfloat 0.0
                SCNTransaction.Begin ()
                SCNTransaction.AnimationDuration <- 1.0
                match solidMeshNode with
                | None -> ()
                | Some x -> x.RemoveFromParentNode ()
                roughMeshNode <- Some node
                rootNode.AddChildNode node
                node.Opacity <- nfloat 1.0
                SCNTransaction.Commit ()
            with ex ->
                this.ShowError ex
            setProgress 1.1f
            k ())
        |> ignore

    member private this.GenerateSolidMesh (k : unit -> unit) =
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
                let mid = sprintf "%s_%d" trainingService.SnapshotId (int project.Settings.Resolution)
                let voxels = Meshes.generateSolidVoxels trainingService.Model trainingService.Data setProgress
                let mesh = Meshes.meshFromVoxels voxels
                if mesh.Triangles.Length > 0 then
                    let meshPathTask = Threading.Tasks.Task.Run(fun () -> project.SaveSolidMeshAsUsdz (mesh, mid)).ContinueWith(fun (t : Threading.Tasks.Task<string>) ->
                        if t.Exception <> null then
                            this.ShowError t.Exception
                        elif t.IsCompletedSuccessfully then
                            this.BeginInvokeOnMainThread (fun () ->
                                viewingMeshPath <- Some t.Result
                                this.UpdateUI ()))
                    ()
                let vnode = this.CreateSolidVoxelsNode voxels
                vnode.Hidden <- not (visibleTypes.HasFlag (ViewObjectType.SolidVoxels))
                let node = SceneKitGeometry.createSolidMeshNode mesh
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
            if verts.Count = 0 then
                SCNNode.Create ()
            else
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
                node.Name <- "Solid Voxels"
                node
        else
            SCNNode.Create ()

    

    member this.HandleBatchData (batchData : BatchTrainingData) =
        let inNode = SceneKitGeometry.createPointCloudNode  UIColor.SystemRed (batchData.InsideSurfacePoints.ToArray()) 
        let outNode = SceneKitGeometry.createPointCloudNode UIColor.SystemGreen (batchData.OutsideSurfacePoints.ToArray()) 
        let freeNode = SceneKitGeometry.createPointCloudNode UIColor.SystemBlue (batchData.FreespacePoints.ToArray())
        let addNode (parent : SCNNode) n =
            let cs = parent.ChildNodes
            if cs.Length > 10 then
                cs.[0].RemoveFromParentNode ()
            parent.AddChildNode n
        inNode.Opacity <- nfloat 0.0
        outNode.Opacity <- nfloat 0.0
        freeNode.Opacity <- nfloat 0.0
        SCNTransaction.Begin ()
        SCNTransaction.AnimationDuration <- 1.5
        addNode insidePointsNode inNode
        addNode outsidePointsNode outNode
        addNode freespacePointsNode freeNode
        inNode.Opacity <- nfloat 1.0
        outNode.Opacity <- nfloat 1.0
        freeNode.Opacity <- nfloat 1.0
        SCNTransaction.SetCompletionBlock (fun () ->
            SCNTransaction.Begin ()
            SCNTransaction.AnimationDuration <- 1.5
            inNode.Opacity <- nfloat 0.0
            outNode.Opacity <- nfloat 0.0
            freeNode.Opacity <- nfloat 0.0
            SCNTransaction.Commit ()
            ())
        SCNTransaction.Commit ()

    member this.ShowFrames () =
        let vc = new FramesViewController (project)
        let nc = new UINavigationController (vc)
        this.PresentPopover (nc, scansButton)
        ()

    member this.ExportSolidMesh () =
        match viewingMeshPath with
        | None -> ()
        | Some path ->
            let url = NSUrl.FromFilename path
            let activityItems : NSObject[] = [| url |]
            let vc = new UIActivityViewController (activityItems, null)
            vc.CompletionHandler <- fun activityType completed ->
                vc.DismissViewController (true, null)
                ()
            this.PresentPopover (vc, exportMeshButton)

    member this.ARSolidMesh () =
        match viewingMeshPath with
        | None -> ()
        | Some path ->
            let vc = new QuickLook.QLPreviewController()
            vc.DataSource <- new QLMeshData(path)
            this.PresentViewController(vc, true, null)

    override this.TouchesBegan (touchSet, e) =
        let touches = touchSet.ToArray<UITouch> ()
        let hitOptions = SCNHitTestOptions(SearchMode = SCNHitTestSearchMode.All,
                                           FirstFoundOnly = false,
                                           BackFaceCulling = true,
                                           IgnoreChildNodes = false,
                                           SortResults = true)
        for t in touches do
            let locInSceneView = t.LocationInView sceneView
            let hits = sceneView.HitTest (locInSceneView, hitOptions)
            //for h in hits do
            //    printfn "HIT %O %s" h.Node h.Node.Name
            let hitBounds = hits |> Array.tryFind (fun h -> h.Node = boundsNode)
            let hitBoundHandle = hits |> Array.tryFind (fun h -> Array.contains h.Node boundsHandles)
            match hitBoundHandle with
            | Some h ->
                let hindex = Array.IndexOf(boundsHandles, h.Node)
                touchState <- SingleTouchOnBoundsHandle hindex
            | _ ->
                match hitBounds with
                | Some h ->
                    touchState <- SingleTouchOnBounds
                | None ->
                    touchState <- NotTouching
                    ()
        this.UpdateUIForTouchState ()

    override this.TouchesMoved (touchSet, e) =
        let touches = touchSet.ToArray<UITouch> ()
        let v3 (dworld : SCNVector3) = System.Numerics.Vector3(dworld.X, dworld.Y, dworld.Z)
        let getDWorld (t : UITouch) (node : SCNNode) =
            let prevLoc = t.PreviousLocationInView sceneView
            let currLoc = t.LocationInView sceneView
            let boundsScreenPosition = sceneView.ProjectPoint (node.WorldPosition)
            let prevWorldPos = sceneView.UnprojectPoint (SCNVector3 (float32 prevLoc.X, float32 prevLoc.Y, boundsScreenPosition.Z))
            let currWorldPos = sceneView.UnprojectPoint (SCNVector3 (float32 currLoc.X, float32 currLoc.Y, boundsScreenPosition.Z))
            currWorldPos - prevWorldPos

        let moveHandle (hindex : int) (dworld : SCNVector3) =
            let mutable minP = project.Settings.ClipTranslation - project.Settings.ClipScale
            let mutable maxP = project.Settings.ClipTranslation + project.Settings.ClipScale
            let oldHPos = boundsPos.[hindex]
            if oldHPos.X < 0.0f then minP.X <- minP.X + dworld.X
                                else maxP.X <- maxP.X + dworld.X
            if oldHPos.Y < 0.0f then minP.Y <- minP.Y + dworld.Y
                                else maxP.Y <- maxP.Y + dworld.Y
            if oldHPos.Z < 0.0f then minP.Z <- minP.Z + dworld.Z
                                else maxP.Z <- maxP.Z + dworld.Z
            let newCenter = (maxP + minP) * 0.5f
            let newScale = (maxP - minP) * 0.5f
            project.Settings.ClipTranslation <- newCenter
            project.Settings.ClipScale <- newScale
            boundsNode.Transform <- project.ClipTransform
            ()

        SCNTransaction.Begin ()
        SCNTransaction.DisableActions <- true
        for t in touches do
            match touchState with
            | NotTouching ->
                // Something weird is happening
                ()
            | SingleTouchOnBounds ->
                let dworld = getDWorld t boundsNode
                printf "DWORLD %A" dworld
                project.Settings.ClipTranslation <- project.Settings.ClipTranslation + v3 dworld
                boundsNode.Transform <- project.ClipTransform
                ()
            | SingleTouchOnBoundsHandle hindex ->
                let node = boundsHandles.[hindex]
                let dworld = getDWorld t node
                printf "HANDLE DWORLD %A" dworld
                //let lprev = node.Position
                //node.WorldPosition <- node.WorldPosition + dworld
                //let lcurr = node.Position
                //let dlocal = lcurr - lprev
                moveHandle hindex dworld
                ()
        SCNTransaction.Commit ()

    member private this.UpdateUIForTouchState () =
        printfn "TOUCH STATE = %A" touchState
        SCNTransaction.Begin ()
        let unColor = UIColor.SystemGray
        let selColor = sceneView.TintColor
        match touchState with
        | NotTouching ->
            boundsNode.Geometry.FirstMaterial.Diffuse.ContentColor <- unColor
            boundsHandles |> Array.iter (fun n -> n.Geometry.FirstMaterial.Diffuse.ContentColor <- unColor)
        | SingleTouchOnBounds ->
            boundsNode.Geometry.FirstMaterial.Diffuse.ContentColor <- selColor
            boundsHandles |> Array.iter (fun n -> n.Geometry.FirstMaterial.Diffuse.ContentColor <- unColor)
        | SingleTouchOnBoundsHandle hindex ->
            boundsNode.Geometry.FirstMaterial.Diffuse.ContentColor <- unColor
            boundsHandles |> Array.iteri (fun i n ->
                let c = if i = hindex then selColor else unColor
                n.Geometry.FirstMaterial.Diffuse.ContentColor <- c)
        SCNTransaction.Commit ()



and TouchState =
    | NotTouching
    | SingleTouchOnBounds
    | SingleTouchOnBoundsHandle of HandleIndex : int


and QLMeshData (meshPath : string) =
    inherit QuickLook.QLPreviewControllerDataSource ()

    override this.PreviewItemCount (_) = nint 1

    override this.GetPreviewItem (_, index) =
        let url = NSUrl.FromFilename meshPath
        let item = new ARKit.ARQuickLookPreviewItem(url)
        item.AllowsContentScaling <- true
        upcast item
