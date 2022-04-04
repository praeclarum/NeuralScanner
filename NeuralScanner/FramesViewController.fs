namespace NeuralScanner

open System
open Foundation
open UIKit


type FramesViewController (project : Project) =
    inherit ListViewController (Title = "Scans")

    let mutable frames : SdfFrame[] = Array.empty

    override this.ViewDidLoad () =
        base.ViewDidLoad ()
        this.NavigationItem.RightBarButtonItems <-
            [|
                //new UIBarButtonItem (UIBarButtonSystemItem.Add, this, new ObjCRuntime.Selector ("addProject:"))
                //new UIBarButtonItem (UIImage.GetSystemImage "questionmark.circle", UIBarButtonItemStyle.Plain, this, new ObjCRuntime.Selector ("showHelp:"))
            |]
        this.RefreshFrames ()

    override this.RowSelected (tableView, indexPath) =
        let frame = frames.[indexPath.Row]
        frame.Visible <- not frame.Visible
        tableView.ReloadRows ([| indexPath |], UITableViewRowAnimation.Automatic)
        tableView.DeselectRow (indexPath, true)
        project.SetChanged "Frames"

    override this.RowsInSection (_, section) =
        nint frames.Length

    override this.GetCell (tableView, indexPath) =
        let cell =
            match tableView.DequeueReusableCell ("P") with
            | null -> new UITableViewCell (UITableViewCellStyle.Default, "P")
            | c -> c
        let frame = frames.[indexPath.Row]
        cell.TextLabel.Text <- frame.Title
        cell.ImageView.Image <- UIImage.FromFile (frame.ImagePath)
        cell.Accessory <- if frame.Visible then UITableViewCellAccessory.Checkmark else UITableViewCellAccessory.None
        cell

    member private this.RefreshFrames () =
        async {
            let newProjects =
                project.GetFrames ()
            
            this.BeginInvokeOnMainThread (fun () ->
                frames <- newProjects
                this.TableView.ReloadData ()
                ())
        }
        |> Async.Start
