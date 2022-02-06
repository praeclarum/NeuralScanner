namespace NeuralScanner

open System
open Foundation
open CoreGraphics
open ObjCRuntime
open UIKit
open ARKit
open SdfKit
open System.Numerics

type RenderViewController () =
    inherit UIViewController ()

    let imageView = new UIImageView()

    let vec3ToUIImage (data : Vec3Data) : UIImage =

        let bytesPerRow = data.Width * 4
        let bytes : byte[] = Array.zeroCreate (data.Height * bytesPerRow)
        let fvalues = data.FloatMemory.Span
        let mutable bi = 0
        let mutable fi = 0
        let n = fvalues.Length
        while fi < n do
            let fr = fvalues.[fi]
            let fg = fvalues.[fi+1]
            let fb = fvalues.[fi+2]
            let br = byte (fr * 255.0f)
            let bg = byte (fg * 255.0f)
            let bb = byte (fb * 255.0f)
            let ba = 255uy
            bytes.[bi] <- br
            bytes.[bi+1] <- bg
            bytes.[bi+2] <- bb
            bytes.[bi+3] <- ba
            fi <- fi + 3
            bi <- bi + 4

        use colorSpace = CGColorSpace.CreateGenericRgb()
        use context = new CGBitmapContext(bytes, nint data.Width, nint data.Height, nint 8, nint bytesPerRow, colorSpace, CGBitmapFlags.NoneSkipLast)
        let cgimage = context.ToImage()
        UIImage.FromImage(cgimage)

    override this.ViewDidLoad() =
        base.ViewDidLoad()
        this.View.BackgroundColor <- UIColor.SystemBackgroundColor
        imageView.Frame <- this.View.Bounds
        imageView.AutoresizingMask <- UIViewAutoresizing.FlexibleDimensions
        imageView.ContentMode <- UIViewContentMode.ScaleAspectFit
        this.View.AddSubview(imageView)
        this.RenderScene()

    member private this.RenderScene () =
        let sdff : SdfFunc = SdfFunc(fun p -> Vector4(0.9f, 0.1f, 0.1f, p.Length() - 1.0f))
        let sdf = sdff.ToSdf()

        let viewTransform =
            Matrix4x4.CreateLookAt(Vector3(2.0f, 1.0f, 2.0f),
                                   Vector3.Zero,
                                   Vector3.UnitY)

        let imageVecs = sdf.ToImage(320, 240, viewTransform)
        let image = vec3ToUIImage imageVecs
        imageView.Image <- image


