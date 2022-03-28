namespace NeuralScanner


open System
open Foundation
open UIKit
open SceneKit

open Praeclarum.AutoLayout


type GettingStartedViewController () =
    inherit UIViewController ()

    // Services

    // UI
    let sceneView = new UIView (Frame = UIScreen.MainScreen.Bounds,
                                BackgroundColor = UIColor.SystemGreen,
                                TranslatesAutoresizingMaskIntoConstraints = false)

    let mutable loadSubs : IDisposable[] = Array.empty


    member this.UpdateUI () =
        ()

    override this.ViewDidLoad () =
        base.ViewDidLoad ()

        this.View.BackgroundColor <- UIColor.SystemBackground

        //
        // Subscribe to events
        //
        loadSubs <-
            [|
            |]
        this.UpdateUI ()

        //
        // Layout
        //

        sceneView.TranslatesAutoresizingMaskIntoConstraints <- false

        let view = this.View
        view.AddSubview (sceneView)

        view.AddConstraints
            [|
                sceneView.LayoutLeft == view.LayoutLeft
                sceneView.LayoutRight == view.LayoutRight
                sceneView.LayoutBottom == view.LayoutBottom
                sceneView.LayoutTop == view.LayoutBottom
            |]

    // This VC is going to be tossed out
    member this.Stop () =
        let subs = loadSubs
        loadSubs <- Array.empty
        for s in subs do
            s.Dispose ()
        ()




