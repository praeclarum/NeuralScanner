namespace NeuralScanner

type ObjectInfo =
    {
        Categories : ObjectCategory[]
        TrainingDataDirectory : string
    }

    static member Load () : ObjectInfo =
        let path = Foundation.NSBundle.MainBundle.PathForResource("ObjectInfo", "json")
        let json = System.IO.File.ReadAllText path
        Newtonsoft.Json.JsonConvert.DeserializeObject<ObjectInfo> (json)

and ObjectCategory =
    {
        CategoryId : string
        Title : string
        Description : string
        Tags : string[]
    }


