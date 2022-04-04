namespace NeuralScanner


open System
open System.Collections.Concurrent

open CoreGraphics
open Foundation
open UIKit
open SceneKit

open Praeclarum.AutoLayout

type IStoppable =
    abstract StopUI : unit -> unit

type Control () =
    inherit UIView (TranslatesAutoresizingMaskIntoConstraints = false, BackgroundColor = UIColor.Clear)
    
type ControlTableCell (control : UIView, height : float) =
    inherit UITableViewCell (UITableViewCellStyle.Default, "C")
    do
        let view = base.ContentView
        control.TranslatesAutoresizingMaskIntoConstraints <- false
        control.AutoresizingMask <- UIViewAutoresizing.FlexibleDimensions
        view.AddSubview control
        view.AddConstraints
            [|
                control.LayoutLeft == view.LayoutMarginsGuide.LayoutLeft
                control.LayoutRight == view.LayoutMarginsGuide.LayoutRight
                control.LayoutTop == view.LayoutMarginsGuide.LayoutTop
                control.LayoutBottom == view.LayoutMarginsGuide.LayoutBottom
            |]
    member this.Control = control

type ToggleButton (title : string) =
    inherit UIButton (TranslatesAutoresizingMaskIntoConstraints = false)
    let selectedConfig = UIButtonConfiguration.FilledButtonConfiguration
    do selectedConfig.Title <- title
    let config = UIButtonConfiguration.BorderedButtonConfiguration
    do config.Title <- title
    do
        base.Configuration <- config
        base.ConfigurationUpdateHandler <- fun b ->
            //printfn "BS %O" b.State
            if b.State.HasFlag (UIControlState.Selected) then
                b.Configuration <- selectedConfig
            else
                b.Configuration <- config
        base.SetTitle (title, UIControlState.Normal)

type ValueSlider (label : string, valueFormat : string,
                  minSliderValue : float32, maxSliderValue,
                  sliderToValue : float32 -> float32, valueToSlider : float32 -> float32) =
    inherit Control ()

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
    let valueView = new UITextField(Text = (sliderToValue minSliderValue).ToString (valueFormat),
                                    ShouldReturn = (fun x -> x.ResignFirstResponder () |> ignore; false),
                                    Font = valueFont,
                                    TextColor = base.TintColor,
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
                    valueView.Text <- v.ToString (valueFormat)
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
                        valueView.Text <- v.ToString (valueFormat)

    member this.UserInteracting = userInteracting

    member this.UpdateValue (v) =
        if not this.UserInteracting then
            this.Value <- v

type ValueSliderTableCell (label : string, valueFormat : string,
                           minSliderValue : float32, maxSliderValue,
                           sliderToValue : float32 -> float32, valueToSlider : float32 -> float32) =
    inherit ControlTableCell (new ValueSlider (label, valueFormat, minSliderValue, maxSliderValue, sliderToValue, valueToSlider), 44.0)
    member this.ValueSlider = this.Control :?> ValueSlider


module VCUtils =
    let showException (ex : exn) (vc : UIViewController) =
        printfn "ERROR: %O" ex
        vc.BeginInvokeOnMainThread (fun _ ->
            let alert = new UIAlertView ("Error", ex.ToString(), null, "OK")
            alert.Show ())

    let presentPopoverFromButtonItem (presentVC : UIViewController) (button : UIBarButtonItem) (vc : UIViewController) =
        presentVC.ModalPresentationStyle <- UIModalPresentationStyle.Popover
        match presentVC.PopoverPresentationController with
        | null -> ()
        | p ->
            p.BarButtonItem <- button
        vc.PresentViewController (presentVC, true, null)

    let presentPopoverFromView (presentVC : UIViewController) (button : UIView) (vc : UIViewController) =
        presentVC.ModalPresentationStyle <- UIModalPresentationStyle.Popover
        match presentVC.PopoverPresentationController with
        | null -> ()
        | p ->
            p.SourceView <- button
            p.SourceRect <- button.Bounds
        vc.PresentViewController (presentVC, true, null)

type BaseViewController () =
    inherit UIViewController ()

    let mutable loadSubs : IDisposable[] = Array.empty

    abstract AddUI : UIView -> NSLayoutConstraint[]
    override this.AddUI _ = Array.empty

    abstract SubscribeUI : unit -> IDisposable[]
    override this.SubscribeUI () = Array.empty

    abstract UpdateUI : unit -> unit
    override this.UpdateUI () = ()

    abstract StopUI : unit -> unit
    interface IStoppable with
        member this.StopUI () = this.StopUI ()
    override this.StopUI () =
        let subs = loadSubs
        loadSubs <- Array.empty
        for s in subs do
            s.Dispose ()

    override this.ViewDidLoad () =
        base.ViewDidLoad ()
        let view = this.View
        view.BackgroundColor <- UIColor.SystemBackground
        view.AddConstraints (this.AddUI view)
        loadSubs <- this.SubscribeUI ()
        this.UpdateUI ()

    member this.ShowError (ex : exn) = VCUtils.showException ex this
    member this.PresentPopover (vc, b : UIBarButtonItem) = VCUtils.presentPopoverFromButtonItem vc b this
    member this.PresentPopover (vc, v : UIView) = VCUtils.presentPopoverFromView vc v this
        

type BaseTableViewController (style : UITableViewStyle) =
    inherit UITableViewController (style)

    let mutable loadSubs : IDisposable[] = Array.empty

    abstract SubscribeUI : unit -> IDisposable[]
    override this.SubscribeUI () = Array.empty

    abstract UpdateUI : unit -> unit
    override this.UpdateUI () = ()

    abstract StopUI : unit -> unit
    interface IStoppable with
        member this.StopUI () = this.StopUI ()
    override this.StopUI () =
        let subs = loadSubs
        loadSubs <- Array.empty
        for s in subs do
            s.Dispose ()

    override this.ViewDidLoad () =
        base.ViewDidLoad ()
        loadSubs <- this.SubscribeUI ()
        this.UpdateUI ()

    member this.ShowError (ex : exn) = VCUtils.showException ex this
    member this.PresentPopover (vc, b : UIBarButtonItem) = VCUtils.presentPopoverFromButtonItem vc b this
    member this.PresentPopover (vc, v : UIView) = VCUtils.presentPopoverFromView vc v this
        

type FormViewController () =
    inherit BaseTableViewController (UITableViewStyle.InsetGrouped)

    let mutable sections : FormSection[] = Array.empty

    override this.ViewDidLoad () =
        base.ViewDidLoad ()
        this.TableView.AllowsSelection <- false

    member this.SetSections (newSections : FormSection[]) =
        sections <- newSections
        if this.IsViewLoaded then
            this.TableView.ReloadData ()
        ()

    override this.NumberOfSections (_) = nint sections.Length
    override this.RowsInSection (_, section) =
        let section = int section
        if 0 <= section && section < sections.Length then
            nint sections.[section].Rows.Length
        else
            nint 0

    override this.TitleForHeader (_, section) =
        let section = int section
        if 0 <= section && section < sections.Length then
            sections.[section].Header
        else
            ""
    override this.TitleForFooter (_, section) =
        let section = int section
        if 0 <= section && section < sections.Length then
            sections.[section].Footer
        else
            ""

    override this.GetCell (tableView, indexPath) =
        let section = int indexPath.Section
        let row = int indexPath.Row
        if 0 <= section && section < sections.Length && 0 <= row && row < sections.[section].Rows.Length then
            let s = sections.[section]
            s.Rows.[row]
        else
            match tableView.DequeueReusableCell ("ErrorCell") with
            | null -> new UITableViewCell (UITableViewCellStyle.Default, "ErrorCell")
            | x -> x


and FormSection =
    {
        Header : string
        Rows : UITableViewCell[]
        Footer : string
    }

type ListViewController () =
    inherit BaseTableViewController (UITableViewStyle.Plain)

