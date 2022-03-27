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
        let trainVC = new TrainViewController()
        let projectsVC = new ProjectsViewController ()
        let projectsNC = new UINavigationController (projectsVC)

        let tabs = new UITabBarController ()
        let tabVCs : UIViewController[] =
            [|
                new UINavigationController (trainVC)
                //new UINavigationController (homeVC)
                new UINavigationController (renderVC)
            |]
        tabs.SetViewControllers (tabVCs, false)

        let split = new UISplitViewController(UISplitViewControllerStyle.DoubleColumn)
        split.PrimaryBackgroundStyle <- UISplitViewControllerBackgroundStyle.Sidebar
        split.SetViewController(projectsNC, UISplitViewControllerColumn.Primary)
        split.SetViewController(trainVC, UISplitViewControllerColumn.Secondary)

        this.Window.RootViewController <- split
        this.Window.MakeKeyAndVisible()
        true
        
       
