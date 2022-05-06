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

        let projectsVC = new ProjectsViewController ()

        let gettingStartedVC = new GettingStartedViewController ()
        let gettingStartedNC = new UINavigationController (gettingStartedVC)

        let split = new UISplitViewController(UISplitViewControllerStyle.DoubleColumn)
        split.PreferredSplitBehavior <- UISplitViewControllerSplitBehavior.Tile
        split.PrimaryBackgroundStyle <- UISplitViewControllerBackgroundStyle.Sidebar
        split.SetViewController(projectsVC, UISplitViewControllerColumn.Primary)
        split.SetViewController(gettingStartedNC, UISplitViewControllerColumn.Secondary)

        this.Window.RootViewController <- split
        this.Window.MakeKeyAndVisible()
        true
        
       
