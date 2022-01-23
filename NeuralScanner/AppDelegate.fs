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

        let vc = new UIViewController(Title = "Home")
        let captureVC = new CaptureViewController()

        let tabs = new UITabBarController ()
        let tabVCs : UIViewController[] =
            [|
                //new UINavigationController (vc)
                new UINavigationController (captureVC)
            |]
        tabs.SetViewControllers (tabVCs, false)

        this.Window.RootViewController <- tabs
        this.Window.MakeKeyAndVisible()
        true
        
       
