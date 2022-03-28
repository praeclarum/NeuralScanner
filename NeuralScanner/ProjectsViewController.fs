namespace NeuralScanner

open System
open Foundation
open UIKit


type ProjectsViewController () =
    inherit UITableViewController (Title = "Projects")
    
    let mutable projects : Project[] = Array.empty        

    override this.ViewDidLoad () =
        base.ViewDidLoad ()
        this.NavigationItem.RightBarButtonItem <- new UIBarButtonItem (UIBarButtonSystemItem.Add, this, new ObjCRuntime.Selector ("addProject:"))
        this.RefreshProjects ()

    override this.RowSelected (tableView, indexPath) =
        let project = projects.[indexPath.Row]
        match this.SplitViewController with
        | null -> ()
        | split ->
            let projectVC = new ProjectViewController (project)
            let projectNC = new UINavigationController (projectVC)
            split.SetViewController (projectNC, UISplitViewControllerColumn.Secondary)

    override this.GetCell (tableView, indexPath) =
        let cell =
            match tableView.DequeueReusableCell ("P") with
            | :? ProjectCell as c -> c
            | _ -> new ProjectCell ()
        let project = projects.[indexPath.Row]
        cell.SetProject (project)
        upcast cell

    override this.RowsInSection (_, section) =
        nint projects.Length

    [<Export("addProject:")>]
    member private this.HandleAdd (sender : NSObject) =
        async {
            ProjectManager.createNewProject ()
            this.RefreshProjects ()
        }
        |> Async.Start

    member private this.RefreshProjects () =
        async {
            let newProjects = ProjectManager.findProjects ()
            
            this.BeginInvokeOnMainThread (fun () ->
                projects <- newProjects
                this.TableView.ReloadData ()
                ())
        }
        |> Async.Start

and ProjectCell () =
    inherit UITableViewCell (UITableViewCellStyle.Subtitle, "P")

    let mutable projectO : Project option = None
    let mutable changedSub : IDisposable option = None

    member this.SetProject (project : Project) =
        // Unsubscribe
        match changedSub with
        | None -> ()
        | Some x -> x.Dispose ()
        // Remember this project
        projectO <- Some project
        // Watch for mutations
        changedSub <- project.Changed.Subscribe (fun _ ->
            this.BeginInvokeOnMainThread (fun _ ->
                this.UpdateUI ())) |> Some
        // Initial update
        this.UpdateUI ()

    member this.UpdateUI () =
        match projectO with
        | None -> ()
        | Some project ->
            this.TextLabel.Text <- project.Name
            this.DetailTextLabel.Text <- sprintf "%d scans" project.NumCaptures


