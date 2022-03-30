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

    let resolution = new ValueSliderTableCell ("Resolution", "0", 10.0f, 1000.0f, id, id)

    let minSize = 0.1f
    let maxSize = 10.0f
    let width = new ValueSliderTableCell ("Width", "0.000", minSize, maxSize, id, id)
    let height = new ValueSliderTableCell ("Height", "0.000", minSize, maxSize, id, id)
    let depth = new ValueSliderTableCell ("Depth", "0.000", minSize, maxSize, id, id)
    let posX = new ValueSliderTableCell ("Center X", "0.000", -maxSize, maxSize, id, id)
    let posY = new ValueSliderTableCell ("Center Y", "0.000", -maxSize, maxSize, id, id)
    let posZ = new ValueSliderTableCell ("Center Z", "0.000", -maxSize, maxSize, id, id)
    let rotY = new ValueSliderTableCell ("Rotation", "0.000", -180.0f, 180.0f, id, id)

    override this.ViewDidLoad () =
        base.ViewDidLoad ()
        this.SetSections
            [|
                {
                    Header = ""
                    Footer = "The output mesh will be constructed by sampling the neural network with the given number of voxels."
                    Rows =
                        [|
                            resolution
                        |]
                }
                {
                    Header = "Boundary Size"
                    Footer = "The output mesh will be constrained to this size."
                    Rows =
                        [|
                            width
                            height
                            depth
                        |]
                }
                {
                    Header = "Boundary Center"
                    Footer = "The output mesh will be centered at this point."
                    Rows =
                        [|
                            posX
                            posY
                            posZ
                        |]
                }
                {
                    Header = "Boundary Rotation"
                    Footer = "The output mesh will be axis-aligned using this rotation."
                    Rows =
                        [|
                            rotY
                        |]
                }
            |]

    override this.UpdateUI () =
        base.UpdateUI ()
        resolution.ValueSlider.UpdateValue (project.Settings.Resolution)
        posX.ValueSlider.UpdateValue (project.Settings.ClipTranslation.X)
        posY.ValueSlider.UpdateValue (project.Settings.ClipTranslation.Y)
        posZ.ValueSlider.UpdateValue (project.Settings.ClipTranslation.Z)
        rotY.ValueSlider.UpdateValue (project.Settings.ClipRotationDegrees.Y)
        if not width.ValueSlider.UserInteracting then
            width.ValueSlider.Value <- project.Settings.ClipScale.X * 2.0f
        if not height.ValueSlider.UserInteracting then
            height.ValueSlider.Value <- project.Settings.ClipScale.Y * 2.0f
        if not depth.ValueSlider.UserInteracting then
            depth.ValueSlider.Value <- project.Settings.ClipScale.Z * 2.0f

    override this.SubscribeUI () =
        [|
            resolution.ValueSlider.ValueChanged.Subscribe (fun v ->
                project.Settings.Resolution <- v
                project.SetModified ("Settings.Resolution"))
            width.ValueSlider.ValueChanged.Subscribe (fun v ->
                let mutable s = project.Settings.ClipScale
                s.X <- v / 2.0f
                project.Settings.ClipScale <- s
                project.SetModified ("Settings.ClipScale")
                ())
            height.ValueSlider.ValueChanged.Subscribe (fun v ->
                let mutable s = project.Settings.ClipScale
                s.Y <- v / 2.0f
                project.Settings.ClipScale <- s
                project.SetModified ("Settings.ClipScale")
                ())
            depth.ValueSlider.ValueChanged.Subscribe (fun v ->
                let mutable s = project.Settings.ClipScale
                s.Z <- v / 2.0f
                project.Settings.ClipScale <- s
                project.SetModified ("Settings.ClipScale"))
            posX.ValueSlider.ValueChanged.Subscribe (fun v ->
                let mutable t = project.Settings.ClipTranslation
                t.X <- v
                project.Settings.ClipTranslation <- t
                project.SetModified ("Settings.ClipTranslation"))
            posY.ValueSlider.ValueChanged.Subscribe (fun v ->
                let mutable t = project.Settings.ClipTranslation
                t.Y <- v
                project.Settings.ClipTranslation <- t
                project.SetModified ("Settings.ClipTranslation"))
            posZ.ValueSlider.ValueChanged.Subscribe (fun v ->
                let mutable t = project.Settings.ClipTranslation
                t.Z <- v
                project.Settings.ClipTranslation <- t
                project.SetModified ("Settings.ClipTranslation"))
            rotY.ValueSlider.ValueChanged.Subscribe (fun v ->
                let mutable r = project.Settings.ClipRotationDegrees
                r.Y <- v
                project.Settings.ClipRotationDegrees <- r
                project.SetModified ("Settings.ClipRotationDegrees"))
        |]

