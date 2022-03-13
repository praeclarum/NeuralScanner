namespace NeuralScanner

open System

open UIKit
open Foundation

[<Register("AppDelegate")>]
type AppDelegate() =
    inherit UIApplicationDelegate()
       
    override val Window = null with get, set

    override this.FinishedLaunching(_, _) =
        this.Window <- new UIWindow(UIScreen.MainScreen.Bounds)

        let homeVC = new UIViewController(Title = "Home")
        let renderVC = new RenderViewController(Title = "Render")
        let captureVC = new CaptureViewController()
        let trainVC = new TrainViewController()

        let tabs = new UITabBarController ()
        let tabVCs : UIViewController[] =
            [|
                new UINavigationController (trainVC)
                //new UINavigationController (homeVC)
                new UINavigationController (renderVC)
                new UINavigationController (captureVC)
            |]
        tabs.SetViewControllers (tabVCs, false)

        this.Window.RootViewController <- tabs
        this.Window.MakeKeyAndVisible()
        true
        
       
