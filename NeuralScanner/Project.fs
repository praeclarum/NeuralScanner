namespace NeuralScanner

open System
open System.IO
open System.Numerics
open MetalTensors

module ProjectDefaults =
    let learningRate = 5.0e-4f
    let resolution = 32.0f


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

    member this.Name with get () = this.Settings.Name
                     and set v = this.Settings.Name <- v; this.SetModified "Name"

    member this.NumCaptures = captureFiles.Length
    member this.NewFrameIndex = this.NumCaptures

    override this.ToString () = sprintf "Project %s" this.Name

    member this.SetModified (property : string) =
        this.Settings.ModifiedUtc <- DateTime.UtcNow
        this.Save ()
        changed.Trigger property

    member this.GetFrame (depthPath : string) : SdfFrame =
        frames.GetOrAdd (depthPath, fun x -> SdfFrame x)

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
                printfn "SAVE TO: %s" settingsPath
                config.Write (settingsPath)
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
                     clipTranslation : Vector3
                    ) =
    inherit Configurable ()

    member val Name = name with get, set
    member val LearningRate = learningRate with get, set
    member val ModifiedUtc = modifiedUtc with get, set
    member val Resolution = resolution with get, set
    member val ClipScale = clipScale with get, set
    member val ClipRotationDegrees = clipRotationDegrees with get, set
    member val ClipTranslation = clipTranslation with get, set

    override this.Config =
        base.Config.Add("name", this.Name).Add("learningRate", this.LearningRate).Add("modifiedUtc", this.ModifiedUtc).Add("resolution", this.Resolution).Add("clipScale", this.ClipScale).Add("clipRotationDegrees", this.ClipRotationDegrees).Add("clipTranslation", this.ClipTranslation)


module ProjectManager =

    let projectsDir = Environment.GetFolderPath (Environment.SpecialFolder.MyDocuments)

    let mutable private loadedProjects : Map<string, Project> = Map.empty

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
                                             Vector3.One,
                                             Vector3.Zero,
                                             Vector3.Zero
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

//type ProjectFramesManager (project : Project) =
//    let mutable depthPaths : string list =
//        []
//    member this.NewFrameIndex = depthPaths.Length
//    member this.AddFrame (depthPath : string) =
//        depthPaths <- depthPath :: depthPaths

//module ProjectFramesManagers =
//    let private services = System.Collections.Concurrent.ConcurrentDictionary<string, ProjectFramesManager> ()
//    let getForProject (project : Project) : ProjectFramesManager =
//        let key = project.ProjectDirectory
//        match services.TryGetValue key with
//        | true, x -> x
//        | _ ->
//            let s = ProjectFramesManager (project)
//            if services.TryAdd (key, s) then
//                s
//            else
//                services.[key]
