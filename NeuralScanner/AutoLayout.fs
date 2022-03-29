module Praeclarum.AutoLayout

open System
open ObjCRuntime

#if __IOS__
open Foundation
open UIKit
type NativeView = UIView
#else
open Foundation
open AppKit
type NativeView = NSView
#endif

/// A first-class value representing a layout attribute on a view.
/// Algebraic operations +, -, *, and / are supported to modify the attribute.
/// Use ==, <==, and >== to create constraints.
/// Use @@ to set the priority of the constraint (1000 is the default).
type LayoutRef =
    {
        View : NSObject
        Attribute : NSLayoutAttribute
        M : nfloat
        C : nfloat
        P : float32
    }
    static member CreateConstraint a b r =
        let m, c, p = b.M / a.M, (b.C - a.C) / a.M, Math.Min (a.P, b.P)
        NSLayoutConstraint.Create (a.View :> NSObject, a.Attribute, r, b.View :> NSObject, b.Attribute, m, c, Priority = p)
    static member FromConstant (c : nfloat) =
        {
            View = null
            Attribute = NSLayoutAttribute.NoAttribute
            M = nfloat 1.0f
            C = c
            P = 1000.0f
        }
    static member FromConstant (c : float) = LayoutRef.FromConstant (nfloat c)
    static member FromConstant (c : float32) = LayoutRef.FromConstant (nfloat c)
    static member ( == ) (a, b) = LayoutRef.CreateConstraint a b NSLayoutRelation.Equal
    static member ( == ) (a, f) = LayoutRef.CreateConstraint a { View = null; Attribute = NSLayoutAttribute.NoAttribute; M = nfloat 1.0; C = f; P = 1000.0f } NSLayoutRelation.Equal
    static member ( == ) (a, f : float) = LayoutRef.CreateConstraint a { View = null; Attribute = NSLayoutAttribute.NoAttribute; M = nfloat 1.0; C = nfloat f; P = 1000.0f } NSLayoutRelation.Equal
    static member ( == ) (a, f : int) = LayoutRef.CreateConstraint a { View = null; Attribute = NSLayoutAttribute.NoAttribute; M = nfloat 1.0; C = nfloat (float f); P = 1000.0f } NSLayoutRelation.Equal
    static member ( >== ) (a, b) = LayoutRef.CreateConstraint a b NSLayoutRelation.GreaterThanOrEqual
    static member ( <== ) (a, b) = LayoutRef.CreateConstraint a b NSLayoutRelation.LessThanOrEqual
    static member ( >== ) (a, c : float) =
        let b = LayoutRef.FromConstant (nfloat c)
        LayoutRef.CreateConstraint a b NSLayoutRelation.GreaterThanOrEqual
    static member ( <== ) (a, c : float) =
        let b = LayoutRef.FromConstant (nfloat c)
        LayoutRef.CreateConstraint a b NSLayoutRelation.LessThanOrEqual
    static member ( @@ ) (r, p) = { r with P = p }
    static member ( @@ ) (r, p : float) = { r with P = float32 p }
    static member ( @@ ) (r, p : nfloat) = { r with P = float32 p }
    static member ( @@ ) (r, p : int) = { r with P = float32 p }
    static member ( * ) (m : nfloat, r) = { r with M = r.M * m; C = r.C * m }
    static member ( * ) (r, m : nfloat) = { r with M = r.M * m; C = r.C * m }
    static member ( * ) (m : float, r) = { r with M = r.M * nfloat m; C = r.C * nfloat m }
    static member ( * ) (r, m : float) = { r with M = r.M * nfloat m; C = r.C * nfloat m }
    static member ( * ) (m : float32, r) = { r with M = r.M * nfloat m; C = r.C * nfloat m }
    static member ( * ) (r, m : float32) = { r with M = r.M * nfloat m; C = r.C * nfloat m }
    static member ( * ) (m : int, r) = { r with M = r.M * nfloat (float m); C = r.C * nfloat (float m) }
    static member ( * ) (r, m : int) = { r with M = r.M * nfloat (float m); C = r.C * nfloat (float m) }
    static member ( / ) (r, m : nfloat) = { r with M = r.M / m; C = r.C / m }
    static member ( / ) (r, m : float) = { r with M = r.M / nfloat m; C = r.C / nfloat m }
    static member ( / ) (r, m : float32) = { r with M = r.M / nfloat m; C = r.C / nfloat m }
    static member ( + ) (c : nfloat, r) = { r with C = r.C + c }
    static member ( + ) (r, c : nfloat) = { r with C = r.C + c }
    static member ( + ) (c : float, r) = { r with C = r.C + nfloat c }
    static member ( + ) (r, c : float) = { r with C = r.C + nfloat c }
    static member ( + ) (c : float32, r) = { r with C = r.C + nfloat c }
    static member ( + ) (r, c : float32) = { r with C = r.C + nfloat c }
    static member ( + ) (c : int, r) = { r with C = r.C + nfloat (float c) }
    static member ( + ) (r, c : int) = { r with C = r.C + nfloat (float c) }
    static member ( - ) (c : nfloat, r) = { r with M = -r.M; C = c - r.C }
    static member ( - ) (r, c : nfloat) = { r with C = r.C - c }
    static member ( - ) (c : float, r) = { r with M = -r.M; C = nfloat c - r.C }
    static member ( - ) (r, c : float) = { r with C = r.C - nfloat c }
    static member ( - ) (c : float32, r) = { r with M = -r.M; C = nfloat c - r.C }
    static member ( - ) (r, c : float32) = { r with C = r.C - nfloat c }
    static member ( - ) (c : int, r) = { r with M = -r.M; C = nfloat (float c) - r.C }
    static member ( - ) (r, c : int) = { r with C = r.C - nfloat (float c) }

#if __IOS__
type UIView with
#else
type NSView with
#endif
    member this.VerticalHuggingPriority 
#if __IOS__
        with get () : float32 = this.ContentHuggingPriority (UILayoutConstraintAxis.Vertical)
        and set v = this.SetContentHuggingPriority (v, UILayoutConstraintAxis.Vertical)
#else
        with get () : float32 = this.GetContentHuggingPriorityForOrientation (NSLayoutConstraintOrientation.Vertical)
        and set v = this.SetContentHuggingPriorityForOrientation (v, NSLayoutConstraintOrientation.Vertical)
#endif
    member this.HorizontalHuggingPriority 
#if __IOS__
        with get () : float32 = this.ContentHuggingPriority (UILayoutConstraintAxis.Horizontal)
        and set v = this.SetContentHuggingPriority (v, UILayoutConstraintAxis.Horizontal)
#else
        with get () : float32 = this.GetContentHuggingPriorityForOrientation (NSLayoutConstraintOrientation.Horizontal)
        and set v = this.SetContentHuggingPriorityForOrientation (v, NSLayoutConstraintOrientation.Horizontal)
#endif

    member this.LayoutBaseline = { View = this; Attribute = NSLayoutAttribute.Baseline; M = nfloat 1.0; C = nfloat 0.0; P = 1000.0f }
    member this.LayoutBottom = { View = this; Attribute = NSLayoutAttribute.Bottom; M = nfloat 1.0; C = nfloat 0.0; P = 1000.0f }
    member this.LayoutCenterX = { View = this; Attribute = NSLayoutAttribute.CenterX; M = nfloat 1.0; C = nfloat 0.0; P = 1000.0f }
    member this.LayoutCenterY = { View = this; Attribute = NSLayoutAttribute.CenterY; M = nfloat 1.0; C = nfloat 0.0; P = 1000.0f }
    member this.LayoutHeight = { View = this; Attribute = NSLayoutAttribute.Height; M = nfloat 1.0; C = nfloat 0.0; P = 1000.0f }
    member this.LayoutLeading = { View = this; Attribute = NSLayoutAttribute.Leading; M = nfloat 1.0; C = nfloat 0.0; P = 1000.0f }
    member this.LayoutLeft = { View = this; Attribute = NSLayoutAttribute.Left; M = nfloat 1.0; C = nfloat 0.0; P = 1000.0f }
    member this.LayoutRight = { View = this; Attribute = NSLayoutAttribute.Right; M = nfloat 1.0; C = nfloat 0.0; P = 1000.0f }
    member this.LayoutTop = { View = this; Attribute = NSLayoutAttribute.Top; M = nfloat 1.0; C = nfloat 0.0; P = 1000.0f }
    member this.LayoutTrailing = { View = this; Attribute = NSLayoutAttribute.Trailing; M = nfloat 1.0; C = nfloat 0.0; P = 1000.0f }
    member this.LayoutWidth = { View = this; Attribute = NSLayoutAttribute.Width; M = nfloat 1.0; C = nfloat 0.0; P = 1000.0f }

        
type UILayoutGuide with
    member this.LayoutBaseline = { View = this; Attribute = NSLayoutAttribute.Baseline; M = nfloat 1.0; C = nfloat 0.0; P = 1000.0f }
    member this.LayoutBottom = { View = this; Attribute = NSLayoutAttribute.Bottom; M = nfloat 1.0; C = nfloat 0.0; P = 1000.0f }
    member this.LayoutCenterX = { View = this; Attribute = NSLayoutAttribute.CenterX; M = nfloat 1.0; C = nfloat 0.0; P = 1000.0f }
    member this.LayoutCenterY = { View = this; Attribute = NSLayoutAttribute.CenterY; M = nfloat 1.0; C = nfloat 0.0; P = 1000.0f }
    member this.LayoutHeight = { View = this; Attribute = NSLayoutAttribute.Height; M = nfloat 1.0; C = nfloat 0.0; P = 1000.0f }
    member this.LayoutLeading = { View = this; Attribute = NSLayoutAttribute.Leading; M = nfloat 1.0; C = nfloat 0.0; P = 1000.0f }
    member this.LayoutLeft = { View = this; Attribute = NSLayoutAttribute.Left; M = nfloat 1.0; C = nfloat 0.0; P = 1000.0f }
    member this.LayoutRight = { View = this; Attribute = NSLayoutAttribute.Right; M = nfloat 1.0; C = nfloat 0.0; P = 1000.0f }
    member this.LayoutTop = { View = this; Attribute = NSLayoutAttribute.Top; M = nfloat 1.0; C = nfloat 0.0; P = 1000.0f }
    member this.LayoutTrailing = { View = this; Attribute = NSLayoutAttribute.Trailing; M = nfloat 1.0; C = nfloat 0.0; P = 1000.0f }
    member this.LayoutWidth = { View = this; Attribute = NSLayoutAttribute.Width; M = nfloat 1.0; C = nfloat 0.0; P = 1000.0f }
