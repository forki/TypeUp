#r @"..\build\FSONParser.dll"

open System
open System.Net
open System.Net.Mail
open FSONParser

type Phone =
    | Mobile of String
    | Office of String
    | Home of String
    | AfterHours of String 
    | Other of String

type Address = {
    Street: String;
    City: String; Region: String; 
    Postal: String option;
    Country: String}

type Jurisdiction = 
    | BC | Alberta | Saskatchewan | Manitoba | Ontario | Quebec 
    | NewBrunswick | NewFoundland | PEI  | Yukon | Canada

type Occupation = 
    | Programmer | Doctor | Pilot
    | Cook | Painter

type Person = {
    Name : string;
    DOB : DateTime;
    eMail : MailAddress;
    Phone : Phone;
    WebSite : Uri;
    IP : IPAddress;
    Occupations : Occupation list;
    Address : Address}

and Company = {
        Name: String;
        WebSite: Uri;
        IncorporationLoc: Jurisdiction;
        BeneficialOwner: LegalEntity}

and LegalEntity = 
    | Person of Person
    | Company of Company
    | Tag of String

and Contract = {
    Number : Int64;
    ID : Guid;
    Start : DateTime;
    Jurisdiction : Jurisdiction;
    Provider : LegalEntity;
    Holder : LegalEntity}
let data = "
Number: 34343
ID:  872ccb13-2e12-4eec-a2f5-ab64b3652b1c
Start: 2009-05-01
Jurisdiction: BC
Provider:
    Company Name: Acme Widgets
    WebSite: http://www.acme.com
    IncorporationLoc: BC
    BeneficialOwner:
        Person Name: Bill Smith
        DOB: 1988-01-20
        eMail: bill@co.com
        Phone: Mobile 604 666 7777
        WebSite: http://www.bill.com
        IP: 127.0.0.1
        Occupations:
        Address: 
            Street: 245 West Howe
            City: Vancouver
            Region: BC
            Postal: V6R-3L6
            Country: Canada
Holder: 
    Person Name: Anne Brown
    DOB: 1998-10-25
    eMail: anne@co.com
    Phone: Office 604 666 8888
    WebSite: http://www.anne.com
    IP: 2001:0:9d38:6abd:2c48:1e19:53ef:ee7e
    Occupations: 
    Address:
        Street: 5553 West 12th Ave
        City: Vancouver
        Region: BC
        Country: Canada"

let contract = (parseFSON typeof<Contract> data) :?> Contract
