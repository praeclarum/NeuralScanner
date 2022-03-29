namespace NeuralScanner


open System
open System.Collections.Concurrent

open CoreGraphics
open Foundation
open UIKit
open SceneKit

open Praeclarum.AutoLayout


type ValueSlider (label : string, valueFormat : string, minSliderValue : float32, maxSliderValue,
                  sliderToValue : float32 -> float32, valueToSlider : float32 -> float32) =
    inherit UIView (BackgroundColor = UIColor.Clear,
                    TranslatesAutoresizingMaskIntoConstraints = false)

    let changed = Event<float32> ()

    let minValue, maxValue =
        let m1 = sliderToValue minSliderValue
        let m2 = sliderToValue maxSliderValue
        min m1 m2, max m1 m2

    let labelFont = UIFont.SystemFontOfSize (UIFont.LabelFontSize)
    let valueFont = UIFont.BoldSystemFontOfSize (UIFont.LabelFontSize)

    let slider = new UISlider (MinValue = minSliderValue, MaxValue = maxSliderValue, Value = minSliderValue, BackgroundColor = UIColor.Clear, TranslatesAutoresizingMaskIntoConstraints = false)
    let labelView = new UILabel(Text = label,
                                Font = labelFont,
                                TranslatesAutoresizingMaskIntoConstraints = false)
    let valueView = new UITextField(Text = String.Format (valueFormat, sliderToValue minSliderValue),
                                    ShouldReturn = (fun x -> x.ResignFirstResponder () |> ignore; false),
                                    Font = valueFont,
                                    BackgroundColor = UIColor.Clear,
                                    TranslatesAutoresizingMaskIntoConstraints = false)

    do
        base.AddSubview (slider)
        base.AddSubview (labelView)
        base.AddSubview (valueView)
        let view = slider.Superview
        
        view.AddConstraints
            [|
                slider.LayoutLeft == view.LayoutLeft
                slider.LayoutRight == view.LayoutRight
                slider.LayoutBottom == view.LayoutBottom
                slider.LayoutHeight == 32
                labelView.LayoutTrailing == slider.LayoutCenterX - 3
                labelView.LayoutBottom == slider.LayoutTop
                labelView.LayoutTop == view.LayoutTop
                valueView.LayoutLeading == slider.LayoutCenterX + 3
                valueView.LayoutTrailing == view.LayoutTrailing
                valueView.LayoutBaseline == labelView.LayoutBaseline
            |]

    let mutable userInteracting = false

    let subs =
        [|
            slider.ValueChanged.Subscribe (fun _ ->
                userInteracting <- true
                try
                    let v = sliderToValue slider.Value
                    valueView.Text <- String.Format (valueFormat, v)
                    changed.Trigger v
                with ex ->
                    printfn "SLIDER ERROR: %O" ex
                userInteracting <- false)
            valueView.EditingChanged.Subscribe (fun _ ->
                userInteracting <- true
                try
                    let v = Math.Clamp (Single.Parse (valueView.Text), minValue, maxValue)
                    slider.Value <- valueToSlider v
                    changed.Trigger v
                with ex ->
                    printfn "SLIDER INPUT ERROR: %O" ex
                userInteracting <- false)
        |]

    member this.ValueChanged = changed.Publish

    member this.Value with get () = sliderToValue slider.Value
                      and set v =
                        slider.Value <- valueToSlider v
                        valueView.Text <- String.Format (valueFormat, v)

    member this.UserInteracting = userInteracting
