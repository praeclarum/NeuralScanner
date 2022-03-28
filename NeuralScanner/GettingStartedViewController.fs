namespace NeuralScanner


open System
open Foundation
open UIKit
open SceneKit

open Praeclarum.AutoLayout


type GettingStartedViewController () =
    inherit BaseViewController (Title = "Getting Started")

    let textView = new UITextView (Frame = UIScreen.MainScreen.Bounds,
                                   BackgroundColor = UIColor.SystemBackground,
                                   TranslatesAutoresizingMaskIntoConstraints = false,
                                   AlwaysBounceVertical = true,
                                   Editable = false    )

    override this.UpdateUI () =
        ()

    override this.AddUI (view) =
        textView.Text <- "Hello world this is a lot of text"
        view.AddSubview (textView)
        [|
            textView.LayoutLeft == view.LayoutLeft
            textView.LayoutRight == view.LayoutRight
            textView.LayoutBottom == view.LayoutBottom
            textView.LayoutTop == view.LayoutTop
        |]





