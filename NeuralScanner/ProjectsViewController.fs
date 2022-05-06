namespace NeuralScanner

open System
open Foundation
open UIKit


type ProjectsViewController () =
    inherit ListViewController (Title = "Objects")
    
    let mutable projects : Project[] = Array.empty        

    override this.ViewDidLoad () =
        base.ViewDidLoad ()
        this.NavigationItem.RightBarButtonItems <-
            [|
                new UIBarButtonItem (UIBarButtonSystemItem.Add, this, new ObjCRuntime.Selector ("addProject:"))
                new UIBarButtonItem (UIImage.GetSystemImage "questionmark.circle", UIBarButtonItemStyle.Plain, this, new ObjCRuntime.Selector ("showHelp:"))
            |]
        this.RefreshProjects ()

    override this.RowSelected (tableView, indexPath) =
        let project = projects.[indexPath.Row]
        let projectVC = new ProjectViewController (project)
        let projectNC = new UINavigationController (projectVC)
        this.ShowDetailViewController (projectNC)

    member this.ShowDetailViewController (detailVC : UIViewController) =
        match this.SplitViewController with
        | null -> ()
        | split ->
            match split.GetViewController (UISplitViewControllerColumn.Secondary) with
            | :? UINavigationController as nc ->
                for vc in nc.ViewControllers do
                    match vc with
                    | :? BaseViewController as vc -> vc.StopUI ()
                    | _ -> ()
            | _ -> ()
            split.SetViewController (detailVC, UISplitViewControllerColumn.Secondary)
            split.HideColumn (UISplitViewControllerColumn.Primary)
            split.ShowColumn (UISplitViewControllerColumn.Secondary)

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

    [<Export("showHelp:")>]
    member private this.HandleHelp (sender : NSObject) =
        let vc = new GettingStartedViewController ()
        let nc = new UINavigationController (vc)
        this.ShowDetailViewController nc

    member private this.RefreshProjects () =
        async {
            let newProjects =
                ProjectManager.findProjects ()
                |> Array.sortByDescending (fun x -> x.ModifiedUtc)
            
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

    static let projectIcon = UIImage.GetSystemImage "cube"

    do
        base.ImageView.Image <- projectIcon

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
            this.DetailTextLabel.Text <- sprintf "%s — %d scans" (project.ModifiedUtc.ToShortDateString()) project.NumCaptures


