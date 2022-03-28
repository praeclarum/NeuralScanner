namespace NeuralScanner

open System
open Foundation
open UIKit

open Praeclarum.AutoLayout

type BaseViewController () =
    inherit UIViewController ()

    let mutable loadSubs : IDisposable[] = Array.empty

    abstract AddUI : UIView -> NSLayoutConstraint[]
    abstract SubscribeUI : unit -> IDisposable[]
    abstract UpdateUI : unit -> unit
    abstract StopUI : unit -> unit

    override this.AddUI _ = Array.empty
    override this.SubscribeUI () = Array.empty
    override this.UpdateUI () = ()
    override this.StopUI () =
        let subs = loadSubs
        loadSubs <- Array.empty
        for s in subs do
            s.Dispose ()
        ()

    override this.ViewDidLoad () =
        base.ViewDidLoad ()

        let view = this.View
        view.BackgroundColor <- UIColor.SystemBackground
        view.AddConstraints (this.AddUI view)

        //
        // Subscribe to events
        //
        loadSubs <- this.SubscribeUI ()
        this.UpdateUI ()

