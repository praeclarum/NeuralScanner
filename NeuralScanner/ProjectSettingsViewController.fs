namespace NeuralScanner


open System
open System.Collections.Concurrent

open CoreGraphics
open Foundation
open UIKit
open SceneKit

open Praeclarum.AutoLayout

type ProjectSettingsViewController (project : Project) =
    inherit FormViewController (Title = "Settings")

    override this.ViewDidLoad () =
        base.ViewDidLoad ()
        this.SetSections
            [|
                {
                    Header = "Clipping Volume"
                    Footer = "The output mesh will be constrained to this volume."
                    Rows =
                        [|
                            new ValueSliderTableCell ("Min X", "{0:0.000}", -5.0f, 5.0f, id, id)
                            new ValueSliderTableCell ("Min Y", "{0:0.000}", -5.0f, 5.0f, id, id)
                            new ValueSliderTableCell ("Min Z", "{0:0.000}", -5.0f, 5.0f, id, id)
                            new ValueSliderTableCell ("Max X", "{0:0.000}", -5.0f, 5.0f, id, id)
                            new ValueSliderTableCell ("Max Y", "{0:0.000}", -5.0f, 5.0f, id, id)
                            new ValueSliderTableCell ("Max Z", "{0:0.000}", -5.0f, 5.0f, id, id)
                        |]
                }
            |]


