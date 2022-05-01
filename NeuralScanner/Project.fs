namespace NeuralScanner

open System
open System.IO
open System.Numerics
open SceneKit
open MetalTensors

module ProjectDefaults =
    let learningRate = 1.0e-4f
    let resolution = 32.0f
    let clipScale = 0.5f

    // Hyperparameters
    let outputScale = 200.0f
    let samplingDistance = 1.0e-3f
    let lossClipDelta = 1.0e-2f * outputScale
    let networkDepth = 8
    let networkWidth = 512
    let batchSize = 2*1024
    let useTanh = false
    let dropoutRate = 0.2f
    let numPositionEncodings = 10

    // Derived
    let outsideDistance = lossClipDelta
    let outsideSdf = Vector4 (1.0f, 1.0f, 1.0f, outsideDistance)


type Project (settings : ProjectSettings, projectDir : string) =
    
    let settingsPath = Path.Combine (projectDir, "Settings.xml")
    let saveMonitor = obj ()

    let mutable captureFiles = Directory.GetFiles (projectDir, "*_Depth.pixelbuffer")

    let frames = System.Collections.Concurrent.ConcurrentDictionary<string, SdfFrame> ()

    let changed = Event<string> ()

    member this.ModifiedUtc = settings.ModifiedUtc

    member this.Changed = changed.Publish

    member this.ProjectDirectory = projectDir
    member this.CaptureDirectory = projectDir
    member this.DepthPaths = captureFiles

    member this.Settings = settings

    member this.Name with get () : string = this.Settings.Name
                     and set v = this.Settings.Name <- v; this.SetModified "Name"
    member this.ExportFileName =
        let n = this.Name.Trim ()
        if n.Length > 0 then n
        else "Untitled"

    member this.NumCaptures = captureFiles.Length
    member this.NewFrameIndex = this.NumCaptures

    member this.SaveSolidMesh (mesh : SdfKit.Mesh, meshId : string) : string =
        let path = Path.Combine (projectDir, sprintf "%s_SolidMesh_%s.obj" this.ExportFileName meshId)
        let tpath = Path.GetTempFileName ()
        mesh.WriteObj (tpath)
        if File.Exists path then
            File.Delete path
        File.Move (tpath, path)
        path

    member this.ClipTransform =
        let st = SCNMatrix4.Scale (this.Settings.ClipScale.X, this.Settings.ClipScale.Y, this.Settings.ClipScale.Z)
        let tt = SCNMatrix4.CreateTranslation (this.Settings.ClipTranslation.X, this.Settings.ClipTranslation.Y, this.Settings.ClipTranslation.Z)
        let rt = SCNMatrix4.CreateRotationY (this.Settings.ClipRotationDegrees.Y * (MathF.PI / 180.0f))
        st * rt * tt

    override this.ToString () = sprintf "Project %s" this.Name

    member this.SetModified (property : string) =
        this.Settings.ModifiedUtc <- DateTime.UtcNow
        this.Save ()
        this.SetChanged property

    member this.SetChanged (property : string) =
        changed.Trigger property

    member this.GetFrame (depthPath : string) : SdfFrame =
        frames.GetOrAdd (depthPath, fun x -> SdfFrame x)

    member this.GetFrames () : SdfFrame[] =
        this.DepthPaths
        |> Array.map this.GetFrame
        |> Array.sortBy (fun x -> x.FrameIndex)

    member this.GetVisibleFrames () : SdfFrame[] =
        this.DepthPaths
        |> Array.map this.GetFrame
        |> Array.filter (fun x -> x.Visible)
        |> Array.sortBy (fun x -> x.FrameIndex)

    member this.AddFrame (frame : SdfFrame) =
        captureFiles <- Array.append captureFiles [| frame.DepthPath |]
        frames.[frame.DepthPath] <- frame
        changed.Trigger "NumCaptures"

    member this.UpdateCaptures () =
        captureFiles <- Directory.GetFiles (projectDir, "*_Depth.pixelbuffer")
        changed.Trigger "NumCaptures"

    member this.Save () =
        let config = this.Settings.Config
        Threading.ThreadPool.QueueUserWorkItem (fun _ ->
            lock saveMonitor (fun () ->
                config.Write (settingsPath)
                printfn "SAVED PROJECT: %s" settingsPath
                //let fileOutput = IO.File.ReadAllText(settingsPath)
                //printfn "FILE:\n%s" fileOutput
                //let newConfig = Config.Read<ProjectSettings> (settingsPath)
                ()))
        |> ignore


and ProjectSettings (name : string,
                     learningRate : float32,
                     modifiedUtc : DateTime,
                     resolution : float32,
                     clipScale : Vector3,
                     clipRotationDegrees : Vector3,
                     clipTranslation : Vector3,
                     [<ConfigDefault (0)>] totalTrainedPoints : int,
                     [<ConfigDefault (0)>] totalTrainedSeconds : int
                    ) =
    inherit Configurable ()

    member val Name = name with get, set
    member val LearningRate = learningRate with get, set
    member val TotalTrainedPoints = totalTrainedPoints with get, set
    member val TotalTrainedSeconds = totalTrainedSeconds with get, set
    member val ModifiedUtc = modifiedUtc with get, set
    member val Resolution = resolution with get, set
    member val ClipScale : Vector3 = clipScale with get, set
    member val ClipRotationDegrees : Vector3 = clipRotationDegrees with get, set
    member val ClipTranslation : Vector3 = clipTranslation with get, set

    override this.Config =
        base.Config.Add("name", this.Name).Add("learningRate", this.LearningRate).Add("modifiedUtc", this.ModifiedUtc).Add("resolution", this.Resolution).Add("clipScale", this.ClipScale).Add("clipRotationDegrees", this.ClipRotationDegrees).Add("clipTranslation", this.ClipTranslation).Add("totalTrainedPoints", this.TotalTrainedPoints).Add("totalTrainedSeconds", this.TotalTrainedSeconds)


module ProjectManager =

    let projectsDir = Environment.GetFolderPath (Environment.SpecialFolder.MyDocuments)

    let mutable private loadedProjects : Map<string, Project> = Map.empty

    do Config.EnableReading<FrameConfig> ()
    do Config.EnableReading<ProjectSettings> ()

    let showError (e : exn) = printfn "ERROR: %O" e

    let loadProject (projectDir : string) : Project =
        match loadedProjects.TryFind projectDir with
        | Some x -> x
        | None ->
            let projectId = Path.GetFileName (projectDir)
            let settingsPath = Path.Combine (projectDir, "Settings.xml")
            let settings =
                try
                    //printfn "LOAD FROM: %s" settingsPath
                    Config.Read<ProjectSettings> (settingsPath)
                with ex ->
                    showError ex
                    let s = ProjectSettings ("Untitled",
                                             ProjectDefaults.learningRate,
                                             DateTime.UtcNow,
                                             ProjectDefaults.resolution,
                                             Vector3.One * ProjectDefaults.clipScale,
                                             Vector3.Zero,
                                             Vector3.Zero,
                                             totalTrainedPoints = 0,
                                             totalTrainedSeconds = 0
                                            )
                    try
                        s.Save (settingsPath)
                    with ex2 -> showError ex2
                    s
            let project = Project (settings, projectDir)
            project.UpdateCaptures ()
            loadedProjects <- loadedProjects.Add (projectDir, project)
            project

    let createNewProject () : unit =
        let newProjectId = Guid.NewGuid().ToString("D")
        let dirName = newProjectId
        let dirPath = Path.Combine(projectsDir, dirName)
        Directory.CreateDirectory(dirPath) |> ignore

    let findProjects () : Project[] =
        let projectDirs =
            IO.Directory.GetDirectories (projectsDir)
        projectDirs
        |> Array.map loadProject
        |> Array.sortBy (fun x -> x.Name)

