namespace NeuralScanner

open System
open System.IO
open MetalTensors

module ProjectDefaults =
    let learningRate = 1.0e-6f


type Project (settings : ProjectSettings, projectDir : string) =

    let mutable captureFiles = [| |]

    let changed = Event<string> ()

    member this.Changed = changed.Publish

    member this.ProjectDirectory = projectDir
    member this.CaptureDirectory = projectDir

    member this.Settings = settings

    member this.Name with get () = this.Settings.Name
                     and set v = this.Settings.Name <- v
                                 this.Save ()
                                 changed.Trigger "Name"

    member this.NumCaptures = captureFiles.Length

    override this.ToString () = sprintf "Project %s" this.Name

    member this.UpdateCaptures () =
        captureFiles <- Directory.GetFiles (projectDir, "*_Depth.pixelbuffer")
        changed.Trigger "NumCaptures"

    member this.Save () =
        let settingsPath = Path.Combine (projectDir, "Settings.xml")
        printfn "SAVE TO: %s" settingsPath
        this.Settings.Save (settingsPath)
        let fileOutput = IO.File.ReadAllText(settingsPath)
        printfn "FILE:\n%s" fileOutput


and ProjectSettings (initialName : string, initialLearningRate : float32) =
    inherit Configurable ()

    member val Name = initialName with get, set
    member val LearningRate = initialLearningRate with get, set

    override this.Config =
        base.Config.Add("initialName", this.Name).Add("initialLearningRate", this.LearningRate)


module ProjectManager =

    let projectsDir = Environment.GetFolderPath (Environment.SpecialFolder.MyDocuments)

    let mutable private loadedProjects : Map<string, Project> = Map.empty

    do Config.EnableReading<ProjectSettings> ()

    let loadProject (projectDir : string) : Project =
        match loadedProjects.TryFind projectDir with
        | Some x -> x
        | None ->
            let projectId = Path.GetFileName (projectDir)
            let settingsPath = Path.Combine (projectDir, "Settings.xml")
            let settings =
                try
                    printfn "LOAD FROM: %s" settingsPath
                    Config.Read<ProjectSettings> (settingsPath)
                with ex ->
                    printfn "LOAD ERROR: %O" ex
                    ProjectSettings ("Untitled", ProjectDefaults.learningRate)
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




