using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Neo4jClient;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.Net.Http.Formatting;
using WEB.Models;
using System.IO;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Org.BouncyCastle.Pkcs;
using iTextSharp.text.pdf.security;
using Org.BouncyCastle.Asn1.Ocsp;

namespace PDF
{
    class Program
    {
        static private GraphClient graphClient;
        static void Main(string[] args)
        {
            graphClient = new GraphClient(new Uri("http://neo4j:born1994@localhost:7474/db/data"));
            graphClient.Connect();
            FormDB();
            var MStream = FormPDF();
            string key = "1";
            SignPdfFile(MStream,new FileStream("Бартков'як Андрій.p12", FileMode.Open), key);
        }
        public static void SignPdfFile(MemoryStream sourceDocument, Stream privateKeyStream, string keyPassword)
        {
            var pk12 = new Pkcs12Store(privateKeyStream, keyPassword.ToCharArray());
            privateKeyStream.Dispose();

            string alias = pk12.Aliases.Cast<string>().FirstOrDefault(pk12.IsKeyEntry);
            var pk = pk12.GetKey(alias).Key;

            Byte[] result;

            var reader = new PdfReader(sourceDocument);
            using (var fout = new MemoryStream())
            {
                using (var stamper = PdfStamper.CreateSignature(reader, fout, '\0'))
                {
                    var appearance = stamper.SignatureAppearance;
                    var ExternalSignature = new PrivateKeySignature(pk, "SHA-512");
                    MakeSignature.SignDetached(appearance, ExternalSignature, new[] { pk12.GetCertificate(alias).Certificate }, null, null, null, 0, CryptoStandard.CADES);

                    result = fout.ToArray();
                    stamper.Close();
                }
            }
            MemoryStream ms = new MemoryStream(result);
            FileStream fs = new FileStream("Result.pdf", FileMode.Create, FileAccess.Write);
            ms.WriteTo(fs);
            fs.Close();
            ms.Close();

        }
        public static void FormDB()
        {
            HttpClient client = new HttpClient();
            string baseUrl = "http://localhost:33083";
            client.BaseAddress = new Uri(baseUrl);

            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            string serviceUrl = "api/ConstituentSets";
            HttpResponseMessage response = client.GetAsync(serviceUrl).Result;
            List<ConstituentAnswer> Constituent = new List<ConstituentAnswer>();
            if (response.IsSuccessStatusCode)
            {
                Constituent = response.Content.ReadAsAsync<IEnumerable<ConstituentAnswer>>().Result.ToList();
            }
            else
            {
                Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
            }
            baseUrl = "http://localhost:45898";
            HttpClient client2Other = new HttpClient();
            client2Other.BaseAddress = new Uri(baseUrl);

            client2Other.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            serviceUrl = "api/expert";
            response = client2Other.GetAsync(serviceUrl).Result;
            List<experts> expertsList = new List<experts>();
            if (response.IsSuccessStatusCode)
            {
                expertsList = response.Content.ReadAsAsync<IEnumerable<experts>>().Result.ToList();
            }
            else
            {
                Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
            }
            for (int i = 0; i < expertsList.Count; i++)
            {
                for (int j = 0; j < Constituent.Count; j++)
                {
                    if (expertsList[i].name == Constituent[j].Name + " " + Constituent[j].MiddleName + " " + Constituent[j].Surname)
                    {
                        var newExpert = new Expert
                        {
                            Id = Constituent[j].IdTaxpayer,
                            Surname = Constituent[j].Surname,
                            Name = Constituent[j].Name,
                            MiddleName = Constituent[j].MiddleName
                        };
                        graphClient.Cypher
                        .Merge("(expert:Expert { Id: {id} })")
                        .OnCreate()
                        .Set("expert = {newExpert}")
                        .WithParams(new
                        {
                            id = newExpert.Id,
                            newExpert
                        })
                        .ExecuteWithoutResults();
                        serviceUrl = "api/order/";
                        response = client2Other.GetAsync(serviceUrl).Result;
                        List<commissionorders> orders = new List<commissionorders>();
                        if (response.IsSuccessStatusCode)
                        {
                            orders = response.Content.ReadAsAsync<IEnumerable<commissionorders>>().Result.ToList();
                        }
                        else
                        {
                            Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
                        }
                        foreach (var item in orders)
                        {
                            if (item.Experts_idExperts == expertsList[i].idExperts)
                            {
                                var neworder = new CommissionOrders
                                {
                                    Name = item.commissionName,
                                    OrderNumber = item.commissionOrderNumber.ToString(),
                                    OrderDate = item.CommissionOrderDate.GetValueOrDefault()
                                };
                                graphClient.Cypher
                                .Merge("(order:CommissionOrders { OrderNumber: {id} })")
                                .OnCreate()
                                .Set("order = {neworder}")
                                .WithParams(new
                                {
                                    id = neworder.OrderNumber,
                                    neworder
                                })
                                .ExecuteWithoutResults();
                                graphClient.Cypher
                                .Match("(expert:Expert)", "(order:CommissionOrders)")
                                .Where((Expert expert) => expert.Id == newExpert.Id)
                                .AndWhere((CommissionOrders order) => order.OrderNumber == neworder.OrderNumber)
                                .CreateUnique("expert-[:signed]->order")
                                .ExecuteWithoutResults();
                            }
                        }
                        serviceUrl = "api/LetterOfAttorneySets";
                        response = client.GetAsync(serviceUrl).Result;
                        List<LetterAnswer> Letters = new List<LetterAnswer>();
                        if (response.IsSuccessStatusCode)
                        {
                            Letters = response.Content.ReadAsAsync<IEnumerable<LetterAnswer>>().Result.ToList();
                        }
                        else
                        {
                            Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
                        }
                        foreach (var item in Letters)
                        {
                            if (item.ConstituentId == Constituent[j].Id)
                            {
                                serviceUrl = "api/RealitySets";
                                response = client.GetAsync(serviceUrl).Result;
                                List<RealityAnswer> Reality = new List<RealityAnswer>();
                                if (response.IsSuccessStatusCode)
                                {
                                    Reality = response.Content.ReadAsAsync<IEnumerable<RealityAnswer>>().Result.ToList();
                                }
                                else
                                {
                                    Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
                                }
                                foreach (var sob in Reality)
                                {
                                    if (sob.Id == item.RealityId)
                                    {
                                        var newReality = new Rielity();
                                        newReality.Id = sob.Id;
                                        if (sob.VehicleRegId == String.Empty || sob.VehicleSerId == String.Empty || sob.VehicleSerId == "null" || sob.VehicleSerId == "null"
                                            || sob.VehicleSerId == null || sob.VehicleSerId == null)
                                        {
                                            newReality.Address = sob.Address;
                                        }
                                        else
                                        {
                                            newReality.RegId = sob.VehicleRegId;
                                            newReality.SerialId = sob.VehicleSerId;
                                        }
                                        newReality.Info = sob.Info;
                                        graphClient.Cypher
                                        .Merge("(reality:Rielity{Id: {id} })")
                                        .OnCreate()
                                        .Set("reality = {newReality}")
                                        .WithParams(new
                                        {
                                            id = newReality.Id,
                                            newReality
                                        })
                                        .ExecuteWithoutResults();
                                        graphClient.Cypher
                                        .Match("(expert:Expert)", "(reality:Rielity)")
                                        .Where((Expert expert) => expert.Id == newExpert.Id)
                                        .AndWhere((Rielity reality) => reality.Id == newReality.Id)
                                        .CreateUnique("expert-[:owns]->reality")
                                        .ExecuteWithoutResults();
                                    }
                                }
                            }
                        }
                    }
                }
            }
            Console.WriteLine("DB was created");
            Console.ReadKey();
        }
        public static MemoryStream FormPDF()
        {
            var document = new Document();
            BaseFont baseFont = RegisterFonts();
            var Stream = new MemoryStream();
            var PDFWritet = PdfWriter.GetInstance(document, Stream);
            document.Open();
            var ExpertsList = graphClient.Cypher
                .Match("(expert:Expert)")
                .Return(expert => expert.As<Expert>())
                .Results
                .ToList();
            foreach (var item in ExpertsList)
            {
                Font font14 = new Font(baseFont, 14);
                Font cat = new Font(baseFont, 14, Font.BOLD);
                Font MainFont = new Font(baseFont, 16, Font.BOLD | Font.UNDERLINE);
                document.Add(new Paragraph("ПІБ: " + item.Name + " " + item.MiddleName + " " + item.Surname, MainFont));
                document.Add(new Paragraph("Ідентифікаційний код: " + item.Id, font14));
                document.Add(new Paragraph(""));
                var OrdersList = graphClient.Cypher
                .Match("(expert:Expert)-[signed]->(order:CommissionOrders)")
                .Where((Expert expert) => expert.Id == item.Id)
                .Return(order => order.As<CommissionOrders>())
                .Results
                .ToList();
                document.Add(new Paragraph("Свідоцтва", cat));
                foreach (var order in OrdersList)
                {
                    document.Add(new Paragraph("Назва: " + order.Name, font14));
                    document.Add(new Paragraph("Номер: " + order.OrderNumber, font14));
                    string date = order.OrderDate.Date.ToString().Remove(11);
                    document.Add(new Paragraph("Дата підписання: " +date , font14));
                    document.Add(Chunk.NEWLINE);
                }
                document.Add(new Paragraph("", font14));
                var RealityList = graphClient.Cypher
                .Match("(expert:Expert)-[owns]->(reality:Rielity)")
                .Where((Expert expert) => expert.Id == item.Id)
                .Return(reality => reality.As<Rielity>())
                .Results
                .ToList();
                document.Add(new Paragraph("Власність", cat));
                foreach (var reality in RealityList)
                {
                    if (reality.RegId == String.Empty
                        || reality.RegId == null)
                    {
                        document.Add(new Paragraph("Нерухомість: ", font14));
                        document.Add(new Paragraph("Адреса: " + reality.Address, font14));
                    }
                    else
                    {
                        document.Add(new Paragraph("Транспортний засіб: ", font14));
                        document.Add(new Paragraph("Серійний номер: " + reality.SerialId, font14));
                        document.Add(new Paragraph("Номер реєстрації: " + reality.RegId, font14));
                    }
                    if (reality.Info == String.Empty)
                    {
                        document.Add(new Paragraph("Додаткова інформація:  " + reality.SerialId, font14));
                    }
                    document.Add(new Paragraph("", font14));
                }
                document.Add(Chunk.NEWLINE);
            }
            PDFWritet.CloseStream = false;
            document.Close();
            Stream.Flush();
            Stream.Position = 0;
            return Stream;
        }
        private static BaseFont RegisterFonts()
        {
            string[] fontNames = { "Calibri.ttf", "Arial.ttf", "Segoe UI.ttf", "Tahoma.ttf" };
            string fontFile = null;
 
            foreach (string name in fontNames)
            {
                fontFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), name);
                if (!File.Exists(fontFile))
                {
                    fontFile = null;
                }
                else break;
            }
            if (fontFile == null)
            {
                throw new FileNotFoundException("No fonts!");
            }
 
            FontFactory.Register(fontFile);
            return BaseFont.CreateFont(fontFile, BaseFont.IDENTITY_H, BaseFont.NOT_EMBEDDED);
        }

    }

    public class OrdersAnswer
    {
        public DateTimeOffset date { get; set; }
        public string number { get; set; }
        public string name { get; set; }
        public int id { get; set; }
    }
    public class Expert
    {
        public string Id { get; set; }
        public string Surname { get; set; }
        public string Name { get; set; }
        public string MiddleName { get; set; }
    }
    public class Rielity
    {
        public int Id { get; set; }
        public string Address { get; set; }
        public string SerialId { get; set; }
        public string RegId { get; set; }
        public string Info { get; set; }
    }
    public class CommissionOrders
    {
        public string Name { get; set; }
        public string OrderNumber { get; set; }
        public DateTimeOffset OrderDate { get; set; }
    }
    public partial class ConstituentAnswer
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string MiddleName { get; set; }
        public string Surname { get; set; }
        public string IdTaxpayer { get; set; }
        public string Info { get; set; }

        public virtual List<int> LetArrey { get; set; }
    }
}
public class LetterAnswer
{
    public int Id { get; set; }
    public string SeriesBlank { get; set; }
    public string IdBlank { get; set; }
    public System.DateTime DateOfCertification { get; set; }
    public System.DateTime ValidityDate { get; set; }
    public bool Irrevocable { get; set; }
    public Nullable<bool> Suspended { get; set; }
    public int RecepcionistId { get; set; }
    public int RealityId { get; set; }
    public int ConstituentId { get; set; }
}

public partial class RealityAnswer
{
    public int Id { get; set; }
    public string Address { get; set; }
    public string VehicleSerId { get; set; }
    public string VehicleRegId { get; set; }
    public string Info { get; set; }

    public virtual List<int> LetterOfAttorney { get; set; }
}