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
                    Header = "Resolution (samples per meter)"
                    Footer = "The output mesh will be constructed by sampling the neural network at the given interval."
                    Rows =
                        [|
                            new ValueSliderTableCell ("Preview", "0", 10.0f, 1000.0f, id, id)
                            new ValueSliderTableCell ("Output", "0", 10.0f, 1000.0f, id, id)
                        |]
                }
                {
                    Header = "Clipping Volume"
                    Footer = "The output mesh will be constrained to this volume."
                    Rows =
                        [|
                            new ValueSliderTableCell ("Min X", "0.000", -5.0f, 5.0f, id, id)
                            new ValueSliderTableCell ("Min Y", "0.000", -5.0f, 5.0f, id, id)
                            new ValueSliderTableCell ("Min Z", "0.000", -5.0f, 5.0f, id, id)
                            new ValueSliderTableCell ("Max X", "0.000", -5.0f, 5.0f, id, id)
                            new ValueSliderTableCell ("Max Y", "0.000", -5.0f, 5.0f, id, id)
                            new ValueSliderTableCell ("Max Z", "0.000", -5.0f, 5.0f, id, id)
                        |]
                }
            |]


