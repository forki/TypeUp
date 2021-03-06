module FSONParser

open System
open System.Reflection
open FParsec
open FSharp.Reflection
open System.Net
open System.Net.Mail

let empty ty = 
    let uc = 
        Reflection.FSharpType.GetUnionCases(typedefof<_ list>.MakeGenericType [|ty|]) 
        |> Seq.filter (fun uc -> uc.Name = "Empty") 
        |> Seq.exactlyOne
    Reflection.FSharpValue.MakeUnion(uc, [||])

let cons element list ty = 
    let uc = 
        Reflection.FSharpType.GetUnionCases(typedefof<_ list>.MakeGenericType [|ty|]) 
        |> Seq.filter (fun uc -> uc.Name = "Cons") 
        |> Seq.exactlyOne
    Reflection.FSharpValue.MakeUnion(uc, [|box element; box list|])

let some element = 
    let ty = element.GetType()
    let uc = 
        Reflection.FSharpType.GetUnionCases(typedefof<_ option>.MakeGenericType [|ty|]) 
        |> Seq.filter (fun uc -> uc.Name = "Some") 
        |> Seq.exactlyOne
    Reflection.FSharpValue.MakeUnion(uc, [|box element|])

let none ty = 
    let uc = 
        Reflection.FSharpType.GetUnionCases(typedefof<_ option>.MakeGenericType [|ty|]) 
        |> Seq.filter (fun uc -> uc.Name = "None") 
        |> Seq.exactlyOne
    Reflection.FSharpValue.MakeUnion(uc, [| |])

let (<!>) (p: Parser<_,_>) label : Parser<_,_> =
    fun stream ->
        printfn "%A: Entering %s" stream.Position label
        let reply = p stream
        printfn "%A: Leaving %s (%A)" stream.Position label reply.Status
        reply
        
let mayThrow (p : Parser<_,_>) : Parser<_,_> =
    fun stream ->
        let state = stream.State        
        try 
            p stream
        with e-> 
            stream.BacktrackTo(state)
            Reply(FatalError, messageError e.Message)

type FSharpType with
    static member IsOption (t : Type) = t.Name = "FSharpOption`1"

type FSharpType with
    static member IsArray (t : Type) = t.IsArray

type FSharpType with
    static member IsList (t : Type) = t.Name = "FSharpList`1"

type MailAddress with
    static member Parse str = MailAddress(str)

type String with
    static member Parse(str) = str

type Uri with
    static member Parse str = Uri(str)

let customFromString (t: Type) str : obj =
    box (t.InvokeMember("FSONParse", BindingFlags.InvokeMethod, null, null, [|box str|]))

let pcustom t =
    let trim (str : string) =
        str.Trim()
    mayThrow(restOfLine false)|>>trim|>>(customFromString t)

let primFromString (t:  Type) str : obj =
    // (t.InvokeMember("Parse", BindingFlags.InvokeMethod, null, null, [|box str|]))

    match t.FullName with
    |"System.Int16" -> box (Int16.Parse(str))
    |"System.Int32" -> box (Int32.Parse(str))
    |"System.Int64" -> box (Int64.Parse(str))
    |"System.UInt16" -> box (UInt16.Parse(str))
    |"System.UInt32" -> box (UInt32.Parse(str))
    |"System.UInt64" -> box (UInt64.Parse(str))
    |"System.Single" -> box (Single.Parse(str))
    |"System.Double" -> box (Double.Parse(str))
    |"System.Decimal" -> box (Decimal.Parse(str))
    |"System.Boolean" -> box (Boolean.Parse(str))
    |"System.Byte" -> box (Byte.Parse(str))
    |"System.SByte" -> box (SByte.Parse(str))
    |"System.Char" -> box (Char.Parse(str))

    |"System.String" -> box str
    |"System.DateTime" -> box (DateTime.Parse str)
    |"System.Guid" -> box (Guid.Parse str)
    |"System.Uri" -> box (Uri.Parse str)
    |"System.Net.IPAddress" -> box (IPAddress.Parse str)
    |"System.Net.Mail.MailAddress" -> box (MailAddress.Parse str)
    |_ -> failwith "Unsupported primative type"

let pprimative t =
    let trim (str : string) =
        str.Trim()
    mayThrow(restOfLine false)|>>trim|>>(primFromString t)

let rec pfieldName (f: Reflection.PropertyInfo) =
    pstring (f.Name + ":")

and pfield (f: Reflection.PropertyInfo) =
    if FSharpType.IsOption f.PropertyType then
        let t = f.PropertyType.GenericTypeArguments |> Seq.exactlyOne
        ((pfieldName f>>.ptype t |>> some) <|>% (none t)) |>> box
    else 
        pfieldName f>>.ptype f.PropertyType

and precord t =
    let makeType vals = 
        FSharpValue.MakeRecord(t,  vals)

    FSharpType.GetRecordFields (t)
        |> Array.map (fun f -> (pfield f.>>spaces) |>> Array.singleton)
        |> Array.reduce (fun p1 p2 -> pipe2 p1 p2 Array.append)
        |>> makeType

and punioninfo  (t: Type) =
    let parsers = 
        FSharpType.GetUnionCases t 
        |> Array.map (fun c -> spaces>>.pstring c.Name.>>spaces>>%c)
    choiceL parsers (sprintf "Expecting a case of %s" t.Name)

and punioncase  (cInfo: UnionCaseInfo) =
    let makeType caseInfo args = 
        FSharpValue.MakeUnion(caseInfo, args)
    let initial : Parser<obj[], unit> = preturn [||]
    let vals = cInfo.GetFields()
            |> Array.map (fun f -> (ptype f.PropertyType.>>spaces) |>> Array.singleton)
            |> Array.fold (fun p1 p2 -> pipe2 p1 p2 Array.append) initial
    vals |>> makeType cInfo

and punion (t : Type)  =
    punioninfo t >>= punioncase 

and pelement (t : Type) =
    spaces>>.pstring "-">>.(ptype t <!> "element")

and parray (t : Type) =
    let elementT = t.GetElementType()
    let toArrayT (elements : obj list)  =
        let arrayT = Array.CreateInstance(elementT, elements.Length)
        for i = (elements.Length - 1) downto 0 do
            arrayT.SetValue(elements.[i], i)
        arrayT

    many (pelement elementT)|>>toArrayT|>>box

and plist (t : Type) =
    let elementT  = t.GenericTypeArguments |> Seq.exactlyOne
    let toListT elements =
        let folder state head =
            cons head  state elementT
        elements |> List.fold folder (empty elementT)

    many (pelement elementT)|>>List.rev|>>toListT|>>box

and ptype(t : Type)  =
    let (|Custom|) (t:Type) = not(isNull(t.GetMethod("FSONParse")))
    let (|Record|) t = FSharpType.IsRecord(t)
    let (|Union|) t = FSharpType.IsUnion(t)
    let (|List|) t = FSharpType.IsList(t)
    let (|Array|) t = FSharpType.IsArray(t)
    
    let (|EMail|) t = t = typeof<MailAddress>        
    let (|URL|) t = t=  typeof<Uri>        
    let (|GUID|) t = t = typeof<Guid>        
    let (|IP|) t = t = typeof<IPAddress>        
    let (|Primative|) t = Type.GetTypeCode(t) <> TypeCode.Object

    spaces >>.
    match t with
    | EMail true | GUID true | URL true | IP true
    | Primative true -> pprimative t
    | Custom true -> pcustom t

    | List true -> plist t
    | Array true -> parray t
    | Record true -> precord t
    | Union true -> punion t
    | _ -> fail "Unsupported type"

let parseFSON t fson = 
    match run (ptype t) fson with
    | Success(result, _, _)   -> result
    | Failure(errorMsg, _, _) -> failwith (sprintf "Failure: %s" errorMsg)
