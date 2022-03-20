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

    override this.GetCell (tableView, indexPath) =
        let cell =
            match tableView.DequeueReusableCell ("P") with
            | null -> new UITableViewCell (UITableViewCellStyle.Subtitle, "P")
            | x -> x
        let project = projects.[indexPath.Row]
        cell.TextLabel.Text <- project.Name
        cell

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




