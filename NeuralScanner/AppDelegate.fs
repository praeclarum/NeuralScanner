namespace NeuralScanner

open System

open UIKit
open Foundation

[<Register("AppDelegate")>]
type AppDelegate() =
    inherit UIApplicationDelegate()
       
    override val Window = null with get, set

    override this.FinishedLaunching(app, _) =
        this.Window <- new UIWindow(UIScreen.MainScreen.Bounds)

        let projectsVC = new ProjectsViewController ()

        let split = new UISplitViewController(UISplitViewControllerStyle.DoubleColumn)
        split.PreferredSplitBehavior <- UISplitViewControllerSplitBehavior.Tile
        split.PrimaryBackgroundStyle <- UISplitViewControllerBackgroundStyle.Sidebar
        split.SetViewController(projectsVC, UISplitViewControllerColumn.Primary)

        if UIDevice.CurrentDevice.UserInterfaceIdiom = UIUserInterfaceIdiom.Phone then
            ()
        else
            let gettingStartedVC = new GettingStartedViewController ()
            let gettingStartedNC = new UINavigationController (gettingStartedVC)
            split.SetViewController(gettingStartedNC, UISplitViewControllerColumn.Secondary)

        this.Window.RootViewController <- split
        this.Window.MakeKeyAndVisible()
        true
        
       
