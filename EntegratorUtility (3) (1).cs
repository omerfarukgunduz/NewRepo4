﻿using AppResObject;
using EDONUSUM.COMMON;
using EDONUSUM.COMMON.eArchiveService;
using EDONUSUM.COMMON.eDespatchService;
using EDONUSUM.COMMON.eInvoiceService;
using EDONUSUM.COMMON.Model;
using EDONUSUM.COMMON.UBLSerializer;
using EDONUSUM.COMMON.Zip;
using EDONUSUM.CONFIG;
using EDONUSUM.CPM.ENTEGRASYON.Results;
using EDONUSUM.REMOTE;
using EDONUSUM.UBL.UBLCreate;
using EDONUSUM.UBL.UBLObject;
using Newtonsoft.Json;
using SchematronController;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Xml.Serialization;
using UblDespatchAdvice;
using UblInvoiceObject;
using UblReceiptAdvice;
using DigitalPlanet = EDONUSUM.COMMON.DIGITALPLANET.WebServices;
using EDM = EDONUSUM.COMMON.EDM.WebServices;
using FIT = EDONUSUM.COMMON.FIT.WebServices;
using QEF = EDONUSUM.COMMON.QEF;
using EDONUSUM.COMMON.QefGetInvoiceService;
using System.Globalization;

namespace EDONUSUM.CPM.ENTEGRASYON
{

    public abstract class Genel
    {
        public abstract string TopluGonder();
        public abstract string Gonder();
        public abstract void Esle();
    }

    public abstract class Genel1 : Genel
    {
        public abstract List<AlinanBelge> AlinanFaturalarListesi();
        public abstract void Indir();
        public abstract void Kabul();
        public abstract void Red();
        public abstract void GonderilenGuncelleDate();
        public abstract void GonderilenGuncelleList();
    }

    public abstract class Genel2 : Genel
    {
        public abstract string Iptal();
        public abstract string Itiraz();

    }

    class deneme : Genel2
    {
        public override void Esle()
        {
            throw new NotImplementedException();
        }

        public override string Gonder()
        {
            throw new NotImplementedException();
        }

        public override string TopluGonder()
        {
            throw new NotImplementedException();
        }
    }

    

    public static class EntegratorUtility
    {
        public  class EFatura :Genel1
        {
            public static List<AlinanBelge> AlinanFaturalarListesi(DateTime StartDate, DateTime EndDate)
            {
                var data = new List<AlinanBelge>();
                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var fitFatura = new FIT.InvoiceWebService();
                        fitFatura.WebServisAdresDegistir();

                        for (DateTime dt = StartDate.Date; dt < EndDate.Date.AddDays(1); dt = dt.AddDays(1))
                        {
                            Connector.m.IssueDate = dt;
                            Connector.m.EndDate = dt.AddDays(1);

                            var fitResult = fitFatura.GelenFaturalar();

                            foreach (var fatura in fitResult)
                            {
                                data.Add(new AlinanBelge
                                {
                                    EVRAKGUID = Guid.Parse(fatura.UUID),
                                    EVRAKNO = fatura.ID,
                                    YUKLEMEZAMAN = fatura.InsertDateTime,
                                    GBETIKET = "",
                                    GBUNVAN = ""
                                });
                            }
                        }
                        break;
                    case "DPLANET":
                        var dpFatura = new DigitalPlanet.InvoiceWebService();
                        dpFatura.WebServisAdresDegistir();
                        dpFatura.Login();

                        foreach (var fatura in dpFatura.GelenEfaturalar(StartDate, EndDate).Invoices)
                        {
                            data.Add(new AlinanBelge
                            {
                                EVRAKGUID = Guid.Parse(fatura.UUID),
                                EVRAKNO = fatura.InvoiceId,
                                YUKLEMEZAMAN = fatura.Issuetime,
                                GBETIKET = fatura.SenderPostBoxName,
                                GBUNVAN = fatura.Partyname
                            });
                        }
                        break;
                    case "QEF":
                        var qefFatura = new QEF.GetInvoiceService();
                        qefFatura.GelenEfaturalar(StartDate, EndDate);

                        foreach (var fatura in qefFatura.GelenEfaturalar(StartDate, EndDate))
                        {
                            data.Add(new AlinanBelge
                            {
                                EVRAKGUID = Guid.Parse(fatura.Value.ettn),
                                EVRAKNO = fatura.Value.ettn,
                                YUKLEMEZAMAN = DateTime.ParseExact(fatura.Value.alimTarihi, "yyyyMMdd", CultureInfo.CurrentCulture),
                                GBETIKET = "",
                                GBUNVAN = ""
                            });
                        }
                        break;
                    case "EDM":
                        break;
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
                return data;
            }
            public static string Gonder(int EVRAKSN)
            {
                DateTime dt = DateTime.Now;
                var doc = GeneralCreator.GetUBLInvoiceData(EVRAKSN);
                bool resend = false;
                if (doc != null)
                {
                    Connector.m.PkEtiketi = doc.PK;
                    var createdUBL = doc.BaseUBL;  // Fatura UBL i oluşturulur

                    if (createdUBL.ProfileID.Value == "IHRACAT")
                        Connector.m.PkEtiketi = "urn:mail:ihracatpk@gtb.gov.tr";
                    else
                        Connector.m.PkEtiketi = doc.PK;

                    if (doc.OLDGUID == "")
                        Entegrasyon.EfagdnUuid(EVRAKSN, createdUBL.UUID.Value);
                    else
                        resend = true;

                    if (UrlModel.SelectedItem == "DPLANET")
                        createdUBL.ID = createdUBL.ID ?? new UblInvoiceObject.IDType { Value = "CPM" + DateTime.Now.Year + EVRAKSN.ToString("000000000") };

                    if (UrlModel.SelectedItem == "QEF" && Connector.m.SablonTip)
                    {
                        var sablon = string.IsNullOrEmpty(doc.ENTSABLON) ? Connector.m.Sablon : doc.ENTSABLON;

                        if (createdUBL.Note == null)
                            createdUBL.Note = new UblInvoiceObject.NoteType[0];

                        var list = createdUBL.Note.ToList();
                        list.Add(new UblInvoiceObject.NoteType { Value = $"#EFN_SERINO_TERCIHI#{sablon}#" }); ;
                        createdUBL.Note = list.ToArray();
                    }
                    //createdUBL.ID = createdUBL.ID ?? new UblInvoiceObject.IDType { Value = "GIB2022000000001 " };

                    InvoiceSerializer serializer = new InvoiceSerializer(); // UBL  XML e dönüştürülür
                    if (Connector.m.SchematronKontrol)
                    {
                        var schematronResult = SchematronChecker.Check(createdUBL, SchematronDocType.eFatura);
                        if (schematronResult.SchemaResult != "Başarılı" || schematronResult.SchematronResult != "Başarılı")
                            throw new Exception(schematronResult.Detail);
                    }
                    var strFatura = serializer.GetXmlAsString(createdUBL); // XML byte tipinden string tipine dönüştürülür

                    var Result = new Results.EFAGDN();

                    switch (UrlModel.SelectedItem)
                    {
                        case "FIT":
                        case "ING":
                        case "INGBANK":
                            var fitEFatura = new FIT.InvoiceWebService();
                            SendUBLResponseType[] fitResult;
                            if (resend)
                                fitResult = fitEFatura.FaturaYenidenGonder(strFatura, createdUBL.UUID.Value, doc.OLDGUID);
                            else
                                fitResult = fitEFatura.FaturaGonder(strFatura, createdUBL.UUID.Value);

                            var envResult = fitEFatura.ZarfDurumSorgula(new[] { fitResult[0].EnvUUID });

                            Result.DurumAciklama = envResult[0].Description;
                            Result.DurumKod = envResult[0].ResponseCode;
                            Result.DurumZaman = envResult[0].IssueDate;
                            Result.EvrakNo = fitResult[0].ID;
                            Result.UUID = fitResult[0].UUID;
                            Result.ZarfUUID = envResult[0].UUID;
                            Result.YanitDurum = doc.METHOD == "TEMELFATURA" ? 1 : 0;

                            Entegrasyon.UpdateEfagdn(Result, EVRAKSN, null);
                            if (Connector.m.DokumanIndir)
                            {
                                var Gonderilen = fitEFatura.FaturaUBLIndir(new[] { Result.UUID });
                                Entegrasyon.UpdateEfados(Gonderilen[0]);
                            }
                            return "e-Fatura başarıyla gönderildi. \nEvrak No: " + fitResult[0].ID;
                        case "DPLANET":
                            var dpEFatura = new DigitalPlanet.InvoiceWebService();
                            var dpResult = dpEFatura.EFaturaGonder(strFatura, createdUBL.IssueDate.Value, doc.ENTSABLON);
                            if (dpResult.ServiceResult == COMMON.dpInvoice.Result.Error)
                            {
                                if (appConfig.Debugging)
                                    MessageBox.Show(JsonConvert.SerializeObject(dpResult), "ServiceResultDescription", MessageBoxButton.OK, MessageBoxImage.Error);

                                throw new Exception(dpResult.ServiceResultDescription);
                            }


                            Result.DurumAciklama = dpResult.ServiceResultDescription;
                            Result.DurumKod = dpResult.ServiceResult == COMMON.dpInvoice.Result.Successful ? "1" : dpResult.ErrorCode + "";
                            Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                            Result.EvrakNo = dpResult.Invoices[0].InvoiceId;
                            Result.UUID = dpResult.Invoices[0].UUID;
                            Result.ZarfUUID = dpResult.InstanceIdentifier;
                            Result.YanitDurum = doc.METHOD == "TEMELFATURA" ? 1 : 0;

                            Entegrasyon.UpdateEfagdn(Result, EVRAKSN, null);
                            if (Connector.m.DokumanIndir)
                            {
                                try
                                {
                                    var Gonderilen = dpEFatura.GonderilenEFaturaIndir(Result.UUID);
                                    if (appConfig.Debugging)
                                    {
                                        if (!Directory.Exists("DP_ReqResp"))
                                            Directory.CreateDirectory("DP_ReqResp");
                                        File.WriteAllBytes($"DP_ReqResp\\File_UpdateEfados_{DateTime.Now:dd_MM_yyyy_HH_mm_ss_ffff}.json", Gonderilen.ReturnValue);
                                    }
                                    Entegrasyon.UpdateEfados(Gonderilen.ReturnValue);
                                }
                                catch (Exception ex)
                                {
                                    if (appConfig.Debugging)
                                        appConfig.DebuggingException(ex);

                                    throw new Exception(ex.Message, ex);
                                }
                            }
                            return "e-Fatura başarıyla gönderildi. \nEvrak No: " + dpResult.Invoices[0].InvoiceId;
                        case "EDM":
                            var edmEFatura = new EDM.InvoiceWebService();
                            createdUBL.ID = new UblInvoiceObject.IDType { Value = "ABC2009123456789" };
                            strFatura = serializer.GetXmlAsString(createdUBL);
                            var edmResult = edmEFatura.EFaturaGonder(strFatura, Connector.m.PkEtiketi, Connector.m.GbEtiketi, Connector.m.VknTckn, createdUBL.AccountingSupplierParty.Party.PartyIdentification[0].ID.Value);

                            var edmInvoice = edmEFatura.GonderilenEFaturaIndir(edmResult.INVOICE[0].UUID);

                            Result.DurumAciklama = edmInvoice[0].HEADER.STATUS_DESCRIPTION;
                            Result.DurumKod = "1";
                            Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                            Result.EvrakNo = edmResult.INVOICE[0].ID;
                            Result.UUID = edmResult.INVOICE[0].UUID;
                            Result.ZarfUUID = edmInvoice[0].HEADER.ENVELOPE_IDENTIFIER ?? "";
                            Result.YanitDurum = doc.METHOD == "TEMELFATURA" ? 1 : 0;

                            Entegrasyon.UpdateEfagdn(Result, EVRAKSN, edmInvoice[0].CONTENT.Value);

                            return "e-Fatura başarıyla gönderildi. \nEvrak No: " + edmResult.INVOICE[0].ID;
                        case "QEF":
                            var qefEFatura = new QEF.InvoiceService();
                            strFatura = serializer.GetXmlAsString(createdUBL);
                            var qefResult = qefEFatura.FaturaGonder(strFatura, EVRAKSN, createdUBL.IssueDate.Value);

                            if (qefResult.durum == 2)
                                throw new Exception("Bir Hata Oluştu!\n" + qefResult.aciklama);

                            Result.DurumAciklama = qefResult.aciklama ?? "";
                            Result.DurumKod = qefResult.gonderimDurumu.ToString();
                            Result.DurumZaman = DateTime.TryParse(qefResult.gonderimTarihi, out DateTime dz) ? dz : new DateTime(1900, 1, 1);
                            Result.EvrakNo = qefResult.belgeNo;
                            Result.UUID = qefResult.ettn;
                            Result.ZarfUUID = "";
                            Result.YanitDurum = doc.METHOD == "TEMELFATURA" ? 1 : 0;

                            Entegrasyon.UpdateEfagdn(Result, EVRAKSN, null);
                            if (Connector.m.DokumanIndir)
                            {
                                var Gonderilen = qefEFatura.FaturaUBLIndir(new[] { Result.UUID });
                                Entegrasyon.UpdateEfados(Gonderilen.First().Value);
                            }
                            return "e-Fatura başarıyla gönderildi. \nEvrak No: " + qefResult.belgeNo;
                        default:
                            throw new Exception("Tanımlı Entegratör Bulunamadı!");
                    }
                }
                return "e-Fatura gönderilemedi!";
            }
            public static void Indir(DateTime day1, DateTime day2)
            {
                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var fitFatura = new FIT.InvoiceWebService();
                        fitFatura.WebServisAdresDegistir();

                        var list = new List<string>();
                        List<GetUBLListResponseType> fitGelen = new List<GetUBLListResponseType>();
                        for (; day1.Date <= day2.Date; day1 = day1.AddDays(1))
                        {
                            Connector.m.IssueDate = new DateTime(day1.Year, day1.Month, day1.Day, 0, 0, 0);
                            Connector.m.EndDate = new DateTime(day1.Year, day1.Month, day1.Day, 23, 59, 59);

                            var tumGelen = fitFatura.GelenFaturalar();
                            if (tumGelen.Count() > 0)
                            {
                                foreach (var fat in tumGelen)
                                {
                                    list.Add(fat.UUID);
                                    fitGelen.Add(fat);
                                }
                            }
                        }

                        List<Results.EFAGLN> fitGelenler = new List<Results.EFAGLN>();
                        foreach (var f in fitGelen)
                        {
                            fitGelenler.Add(new Results.EFAGLN
                            {
                                DurumAciklama = "",
                                DurumKod = "",
                                DurumZaman = f.InsertDateTime,
                                Etiket = f.Identifier,
                                EvrakNo = f.ID,
                                UUID = f.UUID,
                                VergiHesapNo = f.VKN_TCKN,
                                ZarfUUID = f.EnvUUID.ToString()
                            });
                        }
                        var lists = list.Split(20);

                        foreach (var l in lists)
                        {
                            var ubls = fitFatura.GelenFatura(l.ToArray());
                            Entegrasyon.InsertEfagln(ubls, fitGelenler.ToArray());
                        }
                        break;
                    case "DPLANET":
                        var dpFatura = new DigitalPlanet.InvoiceWebService();
                        var dpGelen = dpFatura.GelenEfaturalar(day1, day2);
                        if (dpGelen.ServiceResult == COMMON.dpInvoice.Result.Error)
                            throw new Exception(dpGelen.ServiceResultDescription);

                        var dpGelenler = new List<Results.EFAGLN>();
                        var dpGelenlerByte = new List<byte[]>();
                        foreach (var fatura in dpGelen.Invoices)
                        {
                            dpGelenler.Add(new Results.EFAGLN
                            {
                                DurumAciklama = "",
                                DurumKod = "",
                                DurumZaman = fatura.Issuetime,
                                Etiket = fatura.SenderPostBoxName,
                                EvrakNo = fatura.InvoiceId,
                                UUID = fatura.UUID,
                                VergiHesapNo = fatura.Sendertaxid,
                                ZarfUUID = ""
                            });
                            var bytes = dpFatura.GelenEfatura(fatura.UUID).ReturnValue;
                            dpGelenlerByte.Add(bytes);
                        }
                        Entegrasyon.InsertEfagln(dpGelenlerByte.ToArray(), dpGelenler.ToArray());
                        break;
                    case "EDM":
                        var edmFatura = new EDM.InvoiceWebService();
                        var edmGelen = edmFatura.GelenEfaturalar(day1, day2);

                        var edmGelenler = new List<Results.EFAGLN>();
                        var edmGelenlerByte = new List<byte[]>();
                        foreach (var fatura in edmGelen)
                        {
                            edmGelenler.Add(new Results.EFAGLN
                            {
                                DurumAciklama = fatura.HEADER.GIB_STATUS_CODESpecified ? fatura.HEADER.GIB_STATUS_DESCRIPTION : "0",
                                DurumKod = fatura.HEADER.GIB_STATUS_CODESpecified ? fatura.HEADER.GIB_STATUS_CODE + "" : "0",
                                DurumZaman = fatura.HEADER.ISSUE_DATE,
                                Etiket = fatura.HEADER.FROM,
                                EvrakNo = fatura.ID,
                                UUID = fatura.UUID,
                                VergiHesapNo = fatura.HEADER.SENDER,
                                ZarfUUID = fatura.HEADER.ENVELOPE_IDENTIFIER ?? ""
                            });
                            edmGelenlerByte.Add(fatura.CONTENT.Value);
                        }
                        Entegrasyon.InsertEfagln(edmGelenlerByte.ToArray(), edmGelenler.ToArray());
                        break;
                    case "QEF":
                        var qefGelenler = new List<Results.EFAGLN>();
                        var qefGelenlerByte = new List<byte[]>();

                        var qefFatura = new QEF.GetInvoiceService();
                        foreach (var fatura in qefFatura.GelenEfaturalar(day1, day2))
                        {
                            qefGelenler.Add(new Results.EFAGLN
                            {
                                DurumAciklama = fatura.Value.yanitGonderimCevabiDetayi ?? "",
                                DurumKod = fatura.Value.yanitGonderimCevabiKodu + "",
                                DurumZaman = DateTime.ParseExact(fatura.Key.belgeTarihi, "yyyyMMdd", CultureInfo.CurrentCulture),
                                Etiket = fatura.Key.gonderenEtiket,
                                EvrakNo = fatura.Value.belgeNo,
                                UUID = fatura.Value.ettn,
                                VergiHesapNo = fatura.Key.gonderenVknTckn,
                                ZarfUUID = ""
                            });
                            var ubl = ZipUtility.UncompressFile(qefFatura.GelenUBLIndir(fatura.Value.ettn));
                            qefGelenlerByte.Add(ubl);
                        }
                        Entegrasyon.InsertEfagln(qefGelenlerByte.ToArray(), qefGelenler.ToArray());
                        break;
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
            public static void Kabul(string GUID, string Aciklama)
            {
                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var adp = new RemoteSqlDataAdapter("SELECT DOSYA FROM EFAGLN WITH (NOLOCK) WHERE EVRAKGUID = @GUID", appConfig.GetConnectionStrings()[0]);
                        adp.SelectCommand.Parameters.AddWithValue("@GUID", GUID);

                        DataTable dt = new DataTable();
                        adp.Fill(ref dt);

                        XmlSerializer ser = new XmlSerializer(typeof(UblInvoiceObject.InvoiceType));
                        var party = ((UblInvoiceObject.InvoiceType)ser.Deserialize(new MemoryStream((byte[])dt.Rows[0][0]))).AccountingSupplierParty;

                        var fitUygulamaYaniti = new FIT.InvoiceWebService();
                        fitUygulamaYaniti.WebServisAdresDegistir();
                        var fitResult = fitUygulamaYaniti.UygulamaYanitiGonder(GUID, true, Aciklama, party.Party);
                        Entegrasyon.UpdateEfaglnStatus(new Results.EFAGLN { ZarfUUID = fitResult[0].EnvUUID }, GUID, true, Aciklama);
                        break;
                    case "DPLANET":
                        var dpUygulamaYaniti = new DigitalPlanet.InvoiceWebService();
                        dpUygulamaYaniti.WebServisAdresDegistir();
                        var dpResult = dpUygulamaYaniti.KabulEFatura(GUID);

                        if (dpResult.ServiceResult == COMMON.dpInvoice.Result.Error)
                            throw new Exception(dpResult.ServiceResultDescription);

                        Entegrasyon.UpdateEfaglnStatus(new Results.EFAGLN { ZarfUUID = "" }, GUID, true, Aciklama);
                        break;
                    case "EDM":
                        var edmUygulamaYaniti = new EDM.InvoiceWebService();
                        edmUygulamaYaniti.WebServisAdresDegistir();
                        var edmResult = edmUygulamaYaniti.KabulEFatura(GUID, Aciklama);

                        Entegrasyon.UpdateEfaglnStatus(new Results.EFAGLN { ZarfUUID = "" }, GUID, true, Aciklama);
                        break;
                    case "QEF":
                        var qefAdp = new RemoteSqlDataAdapter("SELECT DOSYA FROM EFAGLN WITH (NOLOCK) WHERE EVRAKGUID = @GUID", appConfig.GetConnectionStrings()[0]);
                        qefAdp.SelectCommand.Parameters.AddWithValue("@GUID", GUID);

                        DataTable qefDt = new DataTable();
                        qefAdp.Fill(ref qefDt);

                        XmlSerializer qefSer = new XmlSerializer(typeof(UblInvoiceObject.InvoiceType));
                        var qefParty = ((UblInvoiceObject.InvoiceType)qefSer.Deserialize(new MemoryStream((byte[])qefDt.Rows[0][0]))).AccountingSupplierParty;

                        var qefUygulamaYaniti = new QEF.GetInvoiceService();
                        var qefResult = qefUygulamaYaniti.YanıtGonder(GUID, true, Aciklama, qefParty.Party);

                        if (qefResult.durum == 2)
                            throw new Exception(qefResult.aciklama);

                        Entegrasyon.UpdateEfaglnStatus(new Results.EFAGLN { ZarfUUID = qefResult.ettn }, GUID, true, Aciklama);
                        break;
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
            public static void Red(string GUID, string Aciklama)
            {
                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var adp = new RemoteSqlDataAdapter("SELECT DOSYA FROM EFAGLN WITH (NOLOCK) WHERE EVRAKGUID = @GUID", appConfig.GetConnectionStrings()[0]);
                        adp.SelectCommand.Parameters.AddWithValue("@GUID", GUID);

                        DataTable dt = new DataTable();
                        adp.Fill(ref dt);

                        XmlSerializer ser = new XmlSerializer(typeof(UblInvoiceObject.InvoiceType));
                        var party = ((UblInvoiceObject.InvoiceType)ser.Deserialize(new MemoryStream((byte[])dt.Rows[0][0]))).AccountingSupplierParty;

                        var fitUygulamaYaniti = new FIT.InvoiceWebService();
                        fitUygulamaYaniti.WebServisAdresDegistir();
                        var fitResult = fitUygulamaYaniti.UygulamaYanitiGonder(GUID, false, Aciklama, party.Party);
                        Entegrasyon.UpdateEfaglnStatus(new Results.EFAGLN { ZarfUUID = fitResult[0].EnvUUID }, GUID, false, Aciklama);
                        break;
                    case "DPLANET":
                        var dpUygulamaYaniti = new DigitalPlanet.InvoiceWebService();
                        dpUygulamaYaniti.WebServisAdresDegistir();
                        var dpResult = dpUygulamaYaniti.RedEFatura(GUID, Aciklama);

                        if (dpResult.ServiceResult == COMMON.dpInvoice.Result.Error)
                            throw new Exception(dpResult.ServiceResultDescription);

                        Entegrasyon.UpdateEfaglnStatus(new Results.EFAGLN { ZarfUUID = "" }, GUID, false, Aciklama);
                        break;
                    case "EDM":
                        var edmUygulamaYaniti = new EDM.InvoiceWebService();
                        edmUygulamaYaniti.WebServisAdresDegistir();
                        var edmResult = edmUygulamaYaniti.RedEFatura(GUID, Aciklama);

                        Entegrasyon.UpdateEfaglnStatus(new Results.EFAGLN { ZarfUUID = "" }, GUID, false, Aciklama);
                        break;
                    case "QEF":
                        var qefAdp = new RemoteSqlDataAdapter("SELECT DOSYA FROM EFAGLN WITH (NOLOCK) WHERE EVRAKGUID = @GUID", appConfig.GetConnectionStrings()[0]);
                        qefAdp.SelectCommand.Parameters.AddWithValue("@GUID", GUID);

                        DataTable qefDt = new DataTable();
                        qefAdp.Fill(ref qefDt);

                        XmlSerializer qefSer = new XmlSerializer(typeof(UblInvoiceObject.InvoiceType));
                        var qefParty = ((UblInvoiceObject.InvoiceType)qefSer.Deserialize(new MemoryStream((byte[])qefDt.Rows[0][0]))).AccountingSupplierParty;

                        var qefUygulamaYaniti = new QEF.GetInvoiceService();
                        var qefResult = qefUygulamaYaniti.YanıtGonder(GUID, false, Aciklama, qefParty.Party);

                        if (qefResult.durum == 2)
                            throw new Exception(qefResult.aciklama);

                        Entegrasyon.UpdateEfaglnStatus(new Results.EFAGLN { UUID = qefResult.ettn }, GUID, false, Aciklama);
                        break;
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
            public static void GonderilenGuncelle(DateTime day1, DateTime day2)
            {
                var Response = new Results.EFAGDN();
                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var fitFatura = new FIT.InvoiceWebService();
                        fitFatura.WebServisAdresDegistir();
                        List<string> yanitUuid = new List<string>();
                        for (; day1.Date <= day2.Date; day1 = day1.AddDays(1))
                        {

                            Connector.m.IssueDate = new DateTime(day1.Year, day1.Month, day1.Day, 0, 0, 0);
                            Connector.m.EndDate = new DateTime(day1.Year, day1.Month, day1.Day, 23, 59, 59);

                            var yanit = fitFatura.GelenUygulamaYanitlari();
                            var gonderilenler = fitFatura.GonderilenFaturalar();
                            //var gonderilenDatalar = fitFatura.FaturaUBLIndir(gonderilenler.Select(elm => elm.UUID).ToArray());

                            Entegrasyon.UpdateEfagdn(gonderilenler);

                            foreach (var ynt in yanit)
                            {
                                yanitUuid.Add(ynt.UUID);
                            }
                        }
                        if (yanitUuid.Count > 0)
                        {
                            foreach (var yanitUuidPart in yanitUuid.Split(20))
                            {
                                var yanit2 = fitFatura.GelenUygulamaYanit(yanitUuidPart.ToArray());

                                foreach (var ynt in yanit2)
                                {
                                    XmlSerializer serializer = new XmlSerializer(typeof(ApplicationResponseType));
                                    var fitResponse = (ApplicationResponseType)serializer.Deserialize(new MemoryStream(ynt));

                                    Response.UUID = fitResponse.DocumentResponse[0].DocumentReference.ID.Value;
                                    Response.DurumAciklama = "";

                                    if (fitResponse.DocumentResponse[0].Response.Description != null)
                                        if (fitResponse.DocumentResponse[0].Response.Description.Length > 0)
                                            Response.DurumAciklama = fitResponse.DocumentResponse[0].Response.Description[0].Value ?? "";

                                    if (fitResponse.Note != null)
                                        if (fitResponse.Note.Length > 0)
                                            Response.DurumAciklama += ": " + fitResponse.Note[0].Value;

                                    Response.DurumKod = fitResponse.DocumentResponse[0].Response.ResponseCode.Value == "KABUL" ? "2" : "3";
                                    Response.DurumZaman = fitResponse.IssueDate.Value;

                                    Entegrasyon.UpdateEfagdnStatus(Response);
                                }
                            }
                        }
                        break;
                    case "DPLANET":
                        var dpFatura = new DigitalPlanet.InvoiceWebService();
                        var dpGelen = dpFatura.GonderilenFaturalar(day1, day2);
                        if (dpGelen.ServiceResult == COMMON.dpInvoice.Result.Error)
                            throw new Exception(dpGelen.ServiceResultDescription);
                        //var dpYanitlar = dpFatura.GelenUygulamaYanitlari();

                        foreach (var fatura in dpGelen.Invoices)
                        {
                            Response.DurumAciklama = fatura.StatusDescription;
                            switch (fatura.StatusCode)
                            {
                                case 9987:
                                    Response.DurumKod = "2";
                                    break;
                                case 9988:
                                    Response.DurumKod = "3";
                                    break;
                                default:
                                    Response.DurumKod = "0";
                                    break;
                            }
                            Response.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Minute, 0);
                            Response.EvrakNo = fatura.InvoiceId;
                            Response.UUID = fatura.UUID;
                            Response.ZarfUUID = "";
                            Entegrasyon.UpdateEfagdnStatus(Response);
                            Entegrasyon.UpdateEfagdnGonderimDurum(fatura.UUID, Queryable.Contains<int>(new[] { 9987, 9988, 54 }.AsQueryable(), fatura.StatusCode) ? 3 : 2);
                        }
                        break;
                    case "EDM":
                        var edmFatura = new EDM.InvoiceWebService();
                        var edmGelen = edmFatura.GonderilenFaturalar(day1, day2);

                        //var edmYanitlar = edmFatura.GelenUygulamaYanitlari(day1, day2);

                        foreach (var fatura in edmGelen)
                        {
                            if (fatura.HEADER.STATUS_DESCRIPTION == "SUCCEED")
                                Entegrasyon.UpdateEfagdnGonderimDurum(fatura.UUID, 3);

                            Response.DurumAciklama = fatura.HEADER.RESPONSE_CODE == "REJECT" ? "" : "";
                            Response.DurumKod = fatura.HEADER.RESPONSE_CODE == "REJECT" ? "3" : (fatura.HEADER.RESPONSE_CODE == "ACCEPT" ? "2" : "");
                            Response.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                            Response.UUID = fatura.UUID;

                            Entegrasyon.UpdateEfagdnStatus(Response);
                        }
                        break;
                    case "QEF":
                        var qefFatura = new QEF.InvoiceService();
                        var qefGelen = qefFatura.GonderilenFaturalar(day1, day2);

                        //var edmYanitlar = edmFatura.GelenUygulamaYanitlari(day1, day2);

                        foreach (var fatura in qefGelen)
                        {
                            if (fatura.Value.durum == 3 && fatura.Value.gonderimDurumu == 4 && fatura.Value.gonderimCevabiKodu == 1200)
                                Entegrasyon.UpdateEfagdnGonderimDurum(fatura.Value.ettn, 3);
                            else if (fatura.Value.gonderimCevabiKodu >= 1200 && fatura.Value.gonderimCevabiKodu < 1300)
                                Entegrasyon.UpdateEfagdnGonderimDurum(fatura.Value.ettn, 2);
                            else if (fatura.Value.gonderimCevabiKodu == 1300)
                                Entegrasyon.UpdateEfagdnGonderimDurum(fatura.Value.ettn, 3);
                            else if (fatura.Value.gonderimDurumu > 2)
                                Entegrasyon.UpdateEfagdnGonderimDurum(fatura.Value.ettn, 0);

                            if (fatura.Key.ettn != null)
                            {
                                Response.DurumAciklama = fatura.Value.yanitDetayi ?? "";
                                Response.DurumKod = fatura.Value.yanitDurumu == 1 ? "3" : (fatura.Value.yanitDurumu == 2 ? "2" : "0");
                                Response.DurumZaman = DateTime.TryParseExact(fatura.Value.yanitTarihi, "yyyyMMdd", CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime dt) ? dt : new DateTime(1900, 1, 1);
                                Response.UUID = fatura.Key.ettn;

                                Entegrasyon.UpdateEfagdnStatus(Response);
                            }
                        }
                        break;
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
            public static void GonderilenGuncelle(List<string> UUIDs)
            {
                var Response = new Results.EFAGDN();
                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var UUID20 = UUIDs.Split(20);
                        foreach (var UUID in UUID20)
                        {
                            var fatura = new FIT.InvoiceWebService();
                            fatura.WebServisAdresDegistir();

                            byte[][] gonderilenler = null;
                            try
                            {
                                for (int i = 0; i < UUID.Count; i++)
                                    UUID[i] = UUID[i].ToLower();
                                gonderilenler = fatura.FaturaUBLIndir(UUID.ToArray());
                            }
                            catch { }
                            try
                            {
                                if (gonderilenler == null)
                                {
                                    for (int i = 0; i < UUID.Count; i++)
                                        UUID[i] = UUID[i].ToUpper();
                                    gonderilenler = fatura.FaturaUBLIndir(UUID.ToArray());
                                }
                            }
                            catch { }

                            foreach (var Gonderilen in gonderilenler)
                                Entegrasyon.UpdateEfados(Gonderilen);
                        }
                        break;
                    case "DPLANET":
                        var dpFatura = new DigitalPlanet.InvoiceWebService();
                        var dpFaturalar = new List<byte[]>();
                        foreach (var UUID in UUIDs)
                        {
                            var dpGonderilen = dpFatura.GonderilenFatura(UUID);
                            if (dpGonderilen.ServiceResult != COMMON.dpInvoice.Result.Error)
                                dpFaturalar.Add(dpGonderilen.ReturnValue);
                        }

                        foreach (var fatura in dpFaturalar)
                            Entegrasyon.UpdateEfados(fatura);
                        break;
                    case "QEF":
                        var qefFatura = new QEF.InvoiceService();
                        var qefGelen = qefFatura.GonderilenFaturalar(UUIDs.ToArray());

                        //var edmYanitlar = edmFatura.GelenUygulamaYanitlari(day1, day2);

                        foreach (var fatura in qefGelen)
                        {
                            if (fatura.durum == 3 && fatura.gonderimDurumu == 4 && fatura.gonderimCevabiKodu == 1200)
                                Entegrasyon.UpdateEfagdnGonderimDurum(fatura.ettn, 3);
                            else if (fatura.gonderimCevabiKodu >= 1200 && fatura.gonderimCevabiKodu < 1300)
                                Entegrasyon.UpdateEfagdnGonderimDurum(fatura.ettn, 2);
                            else if (fatura.gonderimCevabiKodu == 1300)
                                Entegrasyon.UpdateEfagdnGonderimDurum(fatura.ettn, 3);
                            else
                                Entegrasyon.UpdateEfagdnGonderimDurum(fatura.ettn, 0);

                            if (fatura.ettn != null)
                            {
                                Response.DurumAciklama = fatura.yanitDetayi ?? "";
                                Response.DurumKod = fatura.yanitDurumu == 1 ? "3" : (fatura.yanitDurumu == 2 ? "2" : "0");
                                Response.DurumZaman = DateTime.TryParseExact(fatura.yanitTarihi, "", CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime dt2) ? dt2 : new DateTime(1900, 1, 1);
                                Response.UUID = fatura.ettn;

                                Entegrasyon.UpdateEfagdnStatus(Response);
                                Entegrasyon.UpdateEfados(qefFatura.FaturaUBLIndir(new[] { fatura.ettn }).First().Value);
                            }
                        }
                        break;
                    case "EDM":
                        /*
						var edmFatura = new EDM.InvoiceWebService();
						var edmGelen = edmFatura.GonderilenFaturalar(day1, day2);

						//var edmYanitlar = edmFatura.GelenUygulamaYanitlari(day1, day2);

						foreach (var fatura in edmGelen)
						{
							if (fatura.HEADER.STATUS_DESCRIPTION == "SUCCEED")
								Entegrasyon.UpdateEfagdnGonderimDurum(fatura.UUID, 3);

							Response.DurumAciklama = fatura.HEADER.RESPONSE_CODE == "REJECT" ? "" : "";
							Response.DurumKod = fatura.HEADER.RESPONSE_CODE == "REJECT" ? "3" : (fatura.HEADER.RESPONSE_CODE == "ACCEPT" ? "2" : "");
							Response.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
							Response.UUID = fatura.UUID;

							Entegrasyon.UpdateEfagdnStatus(Response);
						}
						*/
                        break;
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
            public static void Esle(List<int> Value, bool DontShow = false)
            {
                var Result = new Results.EFAGDN();
                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":

                        if (Value.Count > 20)
                        {
                            throw new Exception("Eşleme işlemi 20 adet üzerinde yapılamamaktadır.\nTarih aralığı verip çalıştır diyerek durum güncellemesi yapabilirsiniz.");
                        }
                        var fatura = new FIT.InvoiceWebService();
                        fatura.WebServisAdresDegistir();

                        if (UrlModel.SelectedItem == "FIT")
                        {
                            var data = Entegrasyon.GetDataFromEvraksn(Value);

                            foreach (var d in data)
                            {
                                try
                                {
                                    //CpmMessageBox.Show("GUID:" + d["ENTEVRAKGUID"].ToString(), "Debug");
                                    var yanit = fatura.GelenUygulamaYanitByFatura(d["ENTEVRAKGUID"].ToString(), d["VERGIHESAPNO"].ToString(), d["PKETIKET"].ToString());
                                    if (yanit?.Response != null)
                                    {
                                        if (yanit.Response.Length > 0)
                                        {
                                            var ynt = yanit.Response.FirstOrDefault(elm => elm.InvoiceUUID == d["ENTEVRAKGUID"].ToString());
                                            if (ynt != null && ynt.InvResponses != null)
                                            {
                                                if (ynt.InvResponses.Length > 0)
                                                {
                                                    //CpmMessageBox.Show(yanit.Response[0].InvResponses[0].ARType + "; " + yanit.Response[0].InvResponses[0].UUID + "; " + yanit.Response[0].InvoiceUUID, "Debug");

                                                    Result.UUID = ynt.InvoiceUUID;
                                                    Result.DurumAciklama = "";
                                                    if (ynt.InvResponses[0].ARNotes != null)
                                                        foreach (var note in ynt.InvResponses[0].ARNotes)
                                                            Result.DurumAciklama += note + " ";
                                                    Result.DurumKod = ynt.InvResponses[0].ARType == "KABUL" ? "2" : "3";
                                                    Result.DurumZaman = ynt.InvResponses[0].IssueDate;

                                                    Entegrasyon.UpdateEfagdnStatus(Result);
                                                }
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                        //else if(!DontShow)
                        //	CpmMessageBox.Show("Eşle butonu ile Fatura Yanıtları güncellenmemektedir.\nFatura yanıtlarını tarih aralığı vererek çalıştır butonu yardımıyla güncelleyebilirsiniz!", "Dikkat", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

                        var UUIDS = Entegrasyon.GetUUIDFromEvraksn(Value).ToList();

                        List<string> UUIDLower = new List<string>();
                        bool OK = false;
                        Exception exx = null;

                        for (int i = 0; i < UUIDS.Count; i++)
                        {
                            UUIDS[i] = UUIDS[i].ToUpper();
                            UUIDLower.Add(UUIDS[i].ToLower());
                        }

                        try
                        {
                            var gonderilenler = fatura.GonderilenFaturalar(UUIDLower.ToArray());
                            Entegrasyon.UpdateEfagdn(gonderilenler);

                            OK = true;
                        }
                        catch (Exception ex)
                        {
                            exx = ex;
                        }

                        try
                        {
                            if (!OK)
                            {
                                var gonderilenler = fatura.GonderilenFaturalar(UUIDS.ToArray());
                                Entegrasyon.UpdateEfagdn(gonderilenler);

                                OK = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            exx = ex;
                        }

                        if (!OK && exx != null)
                            throw exx;

                        break;
                    case "DPLANET":
                        var dpFatura = new DigitalPlanet.InvoiceWebService();
                        var dpGelen = dpFatura.GonderilenFatura(Entegrasyon.GetUUIDFromEvraksn(Value)[0]);
                        if (dpGelen.ServiceResult == COMMON.dpInvoice.Result.Error)
                            throw new Exception(dpGelen.ServiceResultDescription);

                        Result.DurumAciklama = dpGelen.StatusDescription;
                        Result.DurumKod = dpGelen.StatusCode + "";
                        Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Minute, 0);
                        Result.EvrakNo = dpGelen.InvoiceId;
                        Result.UUID = dpGelen.UUID;
                        Result.ZarfUUID = "";
                        Result.YanitDurum = 0;
                        Entegrasyon.UpdateEfagdn(Result, Value[0], dpGelen.ReturnValue, true);


                        Result.DurumAciklama = "";
                        switch (dpGelen.StatusCode)
                        {
                            case 9987:
                                Result.DurumKod = "2";
                                break;
                            case 9988:
                                Result.DurumKod = "3";
                                break;
                            default:
                                Result.DurumKod = "0";
                                break;
                        }
                        Entegrasyon.UpdateEfagdnStatus(Result);
                        break;
                    case "EDM":
                        var edmFatura = new EDM.InvoiceWebService();
                        var edmGelen = edmFatura.GonderilenEFaturaIndir(Entegrasyon.GetUUIDFromEvraksn(Value)[0]);

                        Result.DurumAciklama = edmGelen[0].HEADER.GIB_STATUS_CODESpecified ? edmGelen[0].HEADER.GIB_STATUS_DESCRIPTION : "0";
                        Result.DurumKod = edmGelen[0].HEADER.GIB_STATUS_CODESpecified ? edmGelen[0].HEADER.GIB_STATUS_CODE + "" : "0";
                        Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                        Result.EvrakNo = edmGelen[0].ID;
                        Result.UUID = edmGelen[0].UUID;
                        Result.ZarfUUID = edmGelen[0].HEADER.ENVELOPE_IDENTIFIER ?? "";
                        Result.YanitDurum = 0;

                        Entegrasyon.UpdateEfagdn(Result, Value[0], edmGelen[0].CONTENT.Value, true);

                        Result.DurumAciklama = edmGelen[0].HEADER.RESPONSE_CODE == "REJECT" ? "" : "";
                        Result.DurumKod = edmGelen[0].HEADER.RESPONSE_CODE == "REJECT" ? "3" : (edmGelen[0].HEADER.RESPONSE_CODE == "ACCEPT" ? "2" : "");
                        Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                        Result.UUID = edmGelen[0].UUID;

                        Entegrasyon.UpdateEfagdnStatus(Result);

                        if (edmGelen[0].HEADER.STATUS_DESCRIPTION == "SUCCEED")
                            Entegrasyon.UpdateEfagdnGonderimDurum(edmGelen[0].UUID, 3);
                        break;
                    case "QEF":
                        var qefFatura = new QEF.InvoiceService();

                        foreach (var d in Value)
                        {
                            var qefGelen = qefFatura.GonderilenEFaturaIndir(Entegrasyon.GetUUIDFromEvraksn(Value), Value.Select(elm => elm + "").ToArray());

                            foreach (var doc in qefGelen)
                            {
                                Result.DurumAciklama = doc.Key.gonderimCevabiDetayi ?? "";
                                Result.DurumKod = doc.Key.gonderimCevabiKodu + "";
                                Result.DurumZaman = DateTime.TryParseExact(doc.Key.alimTarihi, "", CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime dt) ? dt : new DateTime(1900, 1, 1);
                                Result.EvrakNo = doc.Key.belgeNo;
                                Result.UUID = doc.Key.ettn;
                                Result.ZarfUUID = "";
                                Result.YanitDurum = doc.Key.yanitDurumu == 1 ? 3 : (doc.Key.yanitDurumu == 2 ? 2 : 0);

                                Entegrasyon.UpdateEfagdn(Result, d, doc.Value, true);

                                Result.DurumAciklama = doc.Key.yanitDetayi ?? "";
                                Result.DurumKod = doc.Key.yanitDurumu == 1 ? "3" : (doc.Key.yanitDurumu == 2 ? "2" : "0");
                                Result.DurumZaman = DateTime.TryParseExact(doc.Key.yanitTarihi, "", CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime dt2) ? dt2 : new DateTime(1900, 1, 1);
                                Result.UUID = doc.Key.ettn;

                                //File.WriteAllText($"C:\\QEF\\{Guid.NewGuid()}.json", JsonConvert.SerializeObject(doc.Key));
                                //File.WriteAllText($"C:\\QEF\\{Guid.NewGuid()}.json", JsonConvert.SerializeObject(Result));

                                Entegrasyon.UpdateEfagdnStatus(Result);
                                if (doc.Key.durum == 3 && doc.Key.gonderimDurumu == 4 && doc.Key.gonderimCevabiKodu == 1200)
                                    Entegrasyon.UpdateEfagdnGonderimDurum(doc.Key.ettn, 3);
                                else if (doc.Key.gonderimCevabiKodu >= 1200 && doc.Key.gonderimCevabiKodu < 1300)
                                    Entegrasyon.UpdateEfagdnGonderimDurum(doc.Key.ettn, 2);
                                else if (doc.Key.gonderimCevabiKodu == 1300)
                                    Entegrasyon.UpdateEfagdnGonderimDurum(doc.Key.ettn, 3);
                                else if (doc.Key.gonderimDurumu > 2)
                                    Entegrasyon.UpdateEfagdnGonderimDurum(doc.Key.ettn, 0);
                            }
                        }
                        break;
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
            public static void GelenEsle(List<string> uuids)
            {
                var Result = new List<Results.EFAGLN>();
                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var list = new List<string>();
                        var fitFatura = new FIT.InvoiceWebService();

                        var fitFaturalar = fitFatura.ZarfDurumSorgula2(uuids.ToArray());

                        foreach (var f in fitFaturalar)
                        {
                            var res = new Results.EFAGLN
                            {
                                DurumAciklama = f.Description,
                                DurumKod = f.ResponseCode,
                                ZarfUUID = f.UUID
                            };
                            Entegrasyon.UpdateEfagln(res);
                        }
                        break;
                    case "DPLANET":

                        break;
                    case "EDM":

                        break;
                    case "QEF":
                        //var qefFatura = new QEF.GetInvoiceService();

                        //foreach(var uuid in uuids)
                        //{
                        //    var qefEvrak = qefFatura.FaturaYanitSorgula(uuid);

                        //    var res = new Results.EFAGLN
                        //    {
                        //        DurumAciklama = qefEvrak.aciklama ?? "",
                        //        DurumKod = qefEvrak.gonderimDurumu + "",
                        //        ZarfUUID = uuid
                        //    };
                        //    Entegrasyon.UpdateEfagln(res);
                        //}
                        break;
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }

            public override string TopluGonder(List<BaseInvoiceUBL> Faturalar, string ENTSABLON)
            {
                StringBuilder sb = new StringBuilder();
                UBLBaseSerializer serializer = new InvoiceSerializer(); // UBL  XML e dönüştürülür

                var Result = new Results.EFAGDN();

                List<InvoiceType> Faturalar2 = new List<InvoiceType>();

                foreach (var Fatura in Faturalar)
                {
                    int EVRAKSN = Convert.ToInt32(Fatura.BaseUBL.AdditionalDocumentReference.FirstOrDefault(elm => elm.DocumentTypeCode.Value == "CUST_INV_ID")?.ID.Value ?? "0");

                    if (UrlModel.SelectedItem == "DPLANET")
                        Fatura.BaseUBL.ID = Fatura.BaseUBL.ID ?? new UblInvoiceObject.IDType { Value = "CPM" + DateTime.Now.Year + EVRAKSN.ToString("000000000") };

                    if (UrlModel.SelectedItem == "QEF" && Connector.m.SablonTip && Fatura?.BaseUBL != null && Fatura?.BaseUBL?.ID?.Value == "CPM" + DateTime.Now.Year + "000000001")
                    {
                        var sablon = string.IsNullOrEmpty(Fatura.ENTSABLON) ? Connector.m.Sablon : Fatura.ENTSABLON;
                        var list = Fatura?.BaseUBL.Note.ToList();
                        list.Add(new UblInvoiceObject.NoteType { Value = $"#EFN_SERINO_TERCIHI#{sablon}#" });
                        Fatura.BaseUBL.Note = list.ToArray();
                    }
                    //Fatura.BaseUBL.ID = Fatura.BaseUBL.ID ?? new UblInvoiceObject.IDType { Value = "GIB2022000000001 " };

                    Faturalar2.Add(Fatura.BaseUBL);
                }

                if (Connector.m.SchematronKontrol)
                {
                    InvoiceSerializer ser = new InvoiceSerializer();
                    foreach (var Fatura in Faturalar2)
                    {
                        var schematronResult = SchematronChecker.Check(Fatura, SchematronDocType.eFatura);
                        if (schematronResult.SchemaResult != "Başarılı" || schematronResult.SchematronResult != "Başarılı")
                            throw new Exception(schematronResult.Detail);
                    }
                }

                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var fitFatura = new FIT.InvoiceWebService();

                        Dictionary<string, string> strFaturalar = new Dictionary<string, string>();
                        Dictionary<string, string> Alici = new Dictionary<string, string>();

                        foreach (var Fatura in Faturalar)
                        {
                            var strFatura = serializer.GetXmlAsString(Fatura.BaseUBL); // XML byte tipinden string tipine dönüştürülür

                            strFaturalar.Add(Fatura.BaseUBL.UUID.Value, strFatura);
                        }

                        var strFats = strFaturalar.ToList();

                        int i = 0;
                        foreach (var Fatura in Faturalar)
                        {
                            Connector.m.PkEtiketi = Fatura.PK;
                            fitFatura.WebServisAdresDegistir();
                            var result = fitFatura.FaturaGonder(strFats[i].Value, strFats[i].Key);

                            i++;

                            var envResult = fitFatura.ZarfDurumSorgula(new[] { result[0].EnvUUID });

                            foreach (var res in result)
                            {
                                Result.DurumAciklama = envResult[0].Description;
                                Result.DurumKod = envResult[0].ResponseCode;
                                Result.DurumZaman = envResult[0].IssueDate;
                                Result.EvrakNo = res.ID;
                                Result.UUID = res.UUID;
                                Result.ZarfUUID = res.EnvUUID;
                                Result.YanitDurum = Fatura.METHOD == "TEMELFATURA" ? 1 : 0;
                                Entegrasyon.UpdateEfagdn(Result, Convert.ToInt32(res.CustInvID), null);
                                if (Connector.m.DokumanIndir)
                                {
                                    var Gonderilen = fitFatura.FaturaUBLIndir(new[] { Result.UUID });
                                    Entegrasyon.UpdateEfados(Gonderilen[0]);
                                }
                            }

                            sb.AppendLine("e-Fatura başarıyla gönderildi. \nEvrak No: " + result[0].ID);
                        }

                        return sb.ToString();
                    case "DPLANET":
                        var dpFatura = new DigitalPlanet.InvoiceWebService();
                        dpFatura.WebServisAdresDegistir();

                        foreach (var Fatura in Faturalar)
                        {
                            var strFatura = serializer.GetXmlAsString(Fatura.BaseUBL); // XML byte tipinden string tipine dönüştürülür

                            var dpResult = dpFatura.EFaturaGonder(strFatura, Fatura.BaseUBL.IssueDate.Value, ENTSABLON);

                            if (dpResult.ServiceResult == COMMON.dpInvoice.Result.Error)
                            {
                                Connector.m.Hata = true;
                                sb.AppendLine(dpResult.ServiceResultDescription);
                                return sb.ToString();
                            }
                            else
                            {
                                foreach (var doc in dpResult.Invoices)
                                {
                                    var fat = dpFatura.GonderilenEFaturaIndir(doc.UUID);
                                    XmlSerializer deSerializer = new XmlSerializer(typeof(InvoiceType));
                                    InvoiceType inv = (InvoiceType)deSerializer.Deserialize(new MemoryStream(fat.ReturnValue));

                                    Result.DurumAciklama = fat.ServiceResultDescription;
                                    Result.DurumKod = fat.ServiceResult == COMMON.dpInvoice.Result.Successful ? "1" : dpResult.ErrorCode + "";
                                    Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                                    Result.EvrakNo = fat.InvoiceId;
                                    Result.UUID = fat.UUID;
                                    Result.ZarfUUID = dpResult.InstanceIdentifier;
                                    Result.YanitDurum = Faturalar2.FirstOrDefault(elm => elm.UUID.Value == doc.UUID).ProfileID.Value == "TEMELFATURA" ? 1 : 0;

                                    int EVRAKSN = Entegrasyon.GetEvraksnFromUUID(new List<string> { Result.UUID })[0];

                                    Entegrasyon.UpdateEfagdn(Result, EVRAKSN, fat.ReturnValue);
                                    sb.AppendLine("e-Fatura başarıyla gönderildi. \nEvrak No: " + dpResult.Invoices[0].InvoiceId);
                                }
                            }
                        }
                        return sb.ToString();
                    case "EDM":
                        var edmFatura = new EDM.InvoiceWebService();
                        edmFatura.WebServisAdresDegistir();
                        var edmResult = edmFatura.TopluEFaturaGonder(Faturalar2);

                        foreach (var doc in edmResult.INVOICE)
                        {
                            var fat = edmFatura.GonderilenEFaturaIndir(doc.UUID);
                            XmlSerializer deSerializer = new XmlSerializer(typeof(InvoiceType));
                            InvoiceType inv = (InvoiceType)deSerializer.Deserialize(new MemoryStream(fat[0].CONTENT.Value));

                            Result.DurumAciklama = fat[0].HEADER.STATUS_DESCRIPTION;
                            Result.DurumKod = "1";
                            Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                            Result.EvrakNo = edmResult.INVOICE[0].ID;
                            Result.UUID = edmResult.INVOICE[0].UUID;
                            Result.ZarfUUID = fat[0].HEADER.ENVELOPE_IDENTIFIER ?? "";
                            Result.YanitDurum = Faturalar2.FirstOrDefault(elm => elm.UUID.Value == doc.UUID).ProfileID.Value == "TEMELFATURA" ? 1 : 0;

                            Entegrasyon.UpdateEfagdn(Result, Convert.ToInt32(inv.AdditionalDocumentReference.Where(element => element.DocumentTypeCode.Value == "CUST_INV_ID").First().ID.Value), fat[0].CONTENT.Value);
                            sb.AppendLine("e-Fatura başarıyla gönderildi. \nEvrak No: " + Result.EvrakNo);
                        }
                        return sb.ToString();
                    case "QEF":
                        var qefFatura = new QEF.InvoiceService();

                        foreach (var Fatura in Faturalar)
                        {
                            if (Connector.m.SablonTip)
                            {
                                var createdUBL = Fatura.BaseUBL;
                                var sablon = string.IsNullOrEmpty(Fatura.ENTSABLON) ? Connector.m.Sablon : Fatura.ENTSABLON;

                                if (createdUBL.Note == null)
                                    createdUBL.Note = new UblInvoiceObject.NoteType[0];

                                var list = createdUBL.Note.ToList();
                                list.Add(new UblInvoiceObject.NoteType { Value = $"#EFN_SERINO_TERCIHI#{sablon}#" }); ;
                                createdUBL.Note = list.ToArray();
                            }

                            if (Fatura.BaseUBL.ProfileID.Value == "IHRACAT")
                                Connector.m.PkEtiketi = "urn:mail:ihracatpk@gtb.gov.tr";
                            else
                                Connector.m.PkEtiketi = Fatura.PK;

                            var strFatura = serializer.GetXmlAsString(Fatura.BaseUBL); // XML byte tipinden string tipine dönüştürülür

                            var qefResult = qefFatura.FaturaGonder(strFatura, Convert.ToInt32(Fatura.BaseUBL.AdditionalDocumentReference.Where(element => element.DocumentTypeCode.Value == "CUST_INV_ID").First().ID.Value), Fatura.BaseUBL.IssueDate.Value);

                            if (qefResult.durum == 2)
                                throw new Exception("Bir Hata Oluştu!\n" + qefResult.aciklama);

                            Result.DurumAciklama = qefResult.aciklama ?? "";
                            Result.DurumKod = qefResult.gonderimDurumu.ToString();
                            Result.DurumZaman = DateTime.TryParse(qefResult.gonderimTarihi, out DateTime dz) ? dz : new DateTime(1900, 1, 1);
                            Result.EvrakNo = qefResult.belgeNo;
                            Result.UUID = qefResult.ettn;
                            Result.ZarfUUID = "";
                            Result.YanitDurum = Fatura.METHOD == "TEMELFATURA" ? 1 : 0;

                            var qefFaturaResult = qefFatura.FaturaUBLIndir(new[] { Fatura.BaseUBL.UUID.Value });
                            int EVRAKSN = Entegrasyon.GetEvraksnFromUUID(new List<string> { Result.UUID })[0];

                            Entegrasyon.UpdateEfagdn(Result, EVRAKSN, qefFaturaResult.First().Value);
                            sb.AppendLine("e-Fatura başarıyla gönderildi. \nEvrak No: " + Result.EvrakNo);
                        }
                        return sb.ToString();
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }

            public override string Gonder()
            {
                throw new NotImplementedException();
            }

            public override void Esle()
            {
                throw new NotImplementedException();
            }

            public override List<AlinanBelge> AlinanFaturalarListesi()
            {
                throw new NotImplementedException();
            }

            public override void Indir()
            {
                throw new NotImplementedException();
            }

            public override void Kabul()
            {
                throw new NotImplementedException();
            }

            public override void Red()
            {
                throw new NotImplementedException();
            }

            public override void GonderilenGuncelleDate()
            {
                throw new NotImplementedException();
            }

            public override void GonderilenGuncelleList()
            {
                throw new NotImplementedException();
            }
        }






        public static class EArsiv
        {
            public static EArsivYanit Gonder(int EVRAKSN)
            {
                EArsivYanit response = new EArsivYanit();
                var Result = new Results.EFAGDN();
                var doc = GeneralCreator.GetUBLArchiveData(EVRAKSN);
                if (doc != null)
                {
                    Connector.m.PkEtiketi = doc.PK;
                    //doc.BaseUBL.ID.Value = doc.SendId ? doc.BaseUBL.ID.Value : "";
                    var createdUBL = doc.BaseUBL;  // e-Arşiv fatura UBL i oluşturulur

                    if (UrlModel.SelectedItem == "QEF")
                    {
                        if (createdUBL.Note == null)
                            createdUBL.Note = new List<UblInvoiceObject.NoteType>().ToArray();

                        var noteList = createdUBL.Note.ToList();
                        noteList.Add(new UblInvoiceObject.NoteType { Value = "Gönderim Şekli: ELEKTRONIK" });

                        createdUBL.Note = noteList.ToArray();
                    }

                    UBLBaseSerializer serializer = new InvoiceSerializer();  // UBL  XML e dönüştürülür
                    if (UrlModel.SelectedItem == "DPLANET")
                        createdUBL.ID = createdUBL.ID.Value == $"CPM{DateTime.Now.Year}000000001" ? new UblInvoiceObject.IDType { Value = "CPM" + DateTime.Now.Year + EVRAKSN.ToString("000000000") } : createdUBL.ID;

                    //createdUBL.ID = createdUBL.ID.Value == $"CPM{DateTime.Now.Year}000000001" ? new UblInvoiceObject.IDType { Value = "GIB2022000000001" } : createdUBL.ID;

                    if (Connector.m.SchematronKontrol)
                    {
                        var schematronResult = SchematronChecker.Check(createdUBL, SchematronDocType.eArsiv);
                        if (schematronResult.SchemaResult != "Başarılı" || schematronResult.SchematronResult != "Başarılı")
                            throw new Exception(schematronResult.Detail);
                    }
                    var strFatura = serializer.GetXmlAsString(createdUBL); // XML byte tipinden string tipine dönüştürülür

                    Entegrasyon.EfagdnUuid(EVRAKSN, doc.BaseUBL.UUID.Value);
                    switch (UrlModel.SelectedItem)
                    {
                        case "FIT":
                        case "ING":
                        case "INGBANK":
                            var fitEArsiv = new FIT.ArchiveWebService();
                            var fitResult = fitEArsiv.EArsivGonder(strFatura, createdUBL.UUID.Value, doc.SUBE);
                            byte[] ByteData = null;
                            if (fitResult.Result.Result1 == ResultType.SUCCESS)
                            {
                                Connector.m.FaturaUUID = fitResult.preCheckSuccessResults[0].UUID;
                                Connector.m.FaturaID = fitResult.preCheckSuccessResults[0].InvoiceNumber;
                                //var fitFaturaUBL = fitEArsiv.FaturaUBLIndir();
                                //ByteData = fitFaturaUBL.binaryData;

                                Result.DurumAciklama = fitResult.preCheckSuccessResults[0].SuccessDesc;
                                Result.DurumKod = fitResult.preCheckSuccessResults[0].SuccessCode + "";
                                Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                                Result.EvrakNo = fitResult.preCheckSuccessResults[0].InvoiceNumber;
                                Result.UUID = fitResult.preCheckSuccessResults[0].UUID;
                                Result.ZarfUUID = "";
                                Result.YanitDurum = 0;

                                Entegrasyon.UpdateEfagdn(Result, EVRAKSN, null);
                                if (Connector.m.DokumanIndir)
                                {
                                    var Gonderilen = fitEArsiv.ImzaliIndir(Result.UUID, "", 0);
                                    Entegrasyon.UpdateEfados(Gonderilen.binaryData);
                                }
                            }
                            if (fitResult.Result.Result1 == ResultType.SUCCESS)
                            {
                                if (doc.PRINT)
                                {
                                    response.KagitNusha = true;
                                    response.Mesaj = "e-Arşiv Fatura başarıyla gönderildi. \nEvrak No: " + fitResult.preCheckSuccessResults[0].InvoiceNumber + "\nYazdırmak İster Misiniz?";
                                    response.Dosya = ByteData;
                                }
                                else
                                {
                                    response.KagitNusha = false;
                                    response.Mesaj = "e-Arşiv Fatura başarıyla gönderildi. \nEvrak No: " + fitResult.preCheckSuccessResults[0].InvoiceNumber;
                                    response.Dosya = null;
                                }
                            }
                            else
                            {
                                throw new Exception(fitResult.preCheckErrorResults[0].ErrorDesc);
                            }
                            return response;
                        case "DPLANET":
                            var dpEArsiv = new DigitalPlanet.ArchiveWebService();
                            var dpResult = dpEArsiv.EArsivGonder(strFatura, createdUBL.IssueDate.Value);

                            if (dpResult.ServiceResult == COMMON.dpInvoice.Result.Error)
                                throw new Exception(dpResult.ServiceResultDescription);

                            Connector.m.FaturaUUID = dpResult.Invoices[0].UUID;
                            Connector.m.FaturaID = dpResult.Invoices[0].InvoiceId;
                            var dpFaturaUBL = dpEArsiv.EArsivIndir(dpResult.Invoices[0].UUID);
                            ByteData = dpFaturaUBL.ReturnValue;

                            Result.DurumAciklama = dpFaturaUBL.StatusDescription;
                            Result.DurumKod = dpFaturaUBL.StatusCode + "";
                            Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                            Result.EvrakNo = dpFaturaUBL.InvoiceId;
                            Result.UUID = dpFaturaUBL.UUID;
                            Result.ZarfUUID = "";
                            Result.YanitDurum = 0;

                            Entegrasyon.UpdateEfagdn(Result, EVRAKSN, ByteData);

                            if (doc.PRINT)
                            {
                                response.KagitNusha = true;
                                response.Mesaj = "e-Arşiv Fatura başarıyla gönderildi. \nEvrak No: " + Result.EvrakNo + "\nYazdırmak İster Misiniz?";
                                response.Dosya = ByteData;
                            }
                            else
                            {
                                response.KagitNusha = false;
                                response.Mesaj = "e-Arşiv Fatura başarıyla gönderildi. \nEvrak No: " + Result.EvrakNo;
                                response.Dosya = null;
                            }

                            return response;
                        case "EDM":
                            var edmEArsiv = new EDM.ArchiveWebService();
                            var edmResult = edmEArsiv.EArsivGonder(strFatura, createdUBL.AccountingCustomerParty.Party.PartyIdentification[0].ID.Value, Connector.m.VknTckn, createdUBL?.AccountingCustomerParty?.Party?.Contact?.ElectronicMail?.Value ?? "");

                            Connector.m.FaturaUUID = edmResult.INVOICE[0].UUID;
                            Connector.m.FaturaID = edmResult.INVOICE[0].ID;
                            var edmFaturaUBL = edmEArsiv.EArsivIndir(edmResult.INVOICE[0].UUID);
                            ByteData = edmFaturaUBL[0].CONTENT.Value;

                            Result.DurumAciklama = edmFaturaUBL[0].HEADER.STATUS_DESCRIPTION;
                            Result.DurumKod = "";
                            Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                            Result.EvrakNo = edmResult.INVOICE[0].ID;
                            Result.UUID = edmResult.INVOICE[0].UUID;
                            Result.ZarfUUID = "";
                            Result.YanitDurum = 0;

                            Entegrasyon.UpdateEfagdn(Result, EVRAKSN, ByteData);

                            if (doc.PRINT)
                            {
                                response.KagitNusha = true;
                                response.Mesaj = "e-Arşiv Fatura başarıyla gönderildi. \nEvrak No: " + Result.EvrakNo + "\nYazdırmak İster Misiniz?";
                                response.Dosya = ByteData;
                            }
                            else
                            {
                                response.KagitNusha = false;
                                response.Mesaj = "e-Arşiv Fatura başarıyla gönderildi. \nEvrak No: " + Result.EvrakNo;
                                response.Dosya = null;
                            }

                            return response;
                        case "QEF":
                            var qefEArsiv = new QEF.ArchiveService();
                            doc.SUBE = doc.SUBE == "default" ? "DFLT" : doc.SUBE;
                            var qefResult = qefEArsiv.EArsivGonder(strFatura, Connector.m.VknTckn, EVRAKSN, doc.SUBE, doc.BaseUBL.IssueDate.Value.Date);

                            if (qefResult.Result.resultText != "İşlem başarılı.")
                                throw new Exception(qefResult.Result.resultText);

                            ByteData = qefResult.Belge.belgeIcerigi;

                            Result.DurumAciklama = qefResult.Result.resultText;
                            Result.DurumKod = qefResult.Result.resultCode;
                            Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                            Result.EvrakNo = qefResult.Result.resultExtra.First(elm => elm.key.ToString() == "faturaNo").value.ToString();
                            Result.UUID = qefResult.Result.resultExtra.First(elm => elm.key.ToString() == "uuid").value.ToString();
                            Result.ZarfUUID = "";
                            Result.YanitDurum = 0;

                            Entegrasyon.UpdateEfagdn(Result, EVRAKSN, ByteData);

                            if (doc.PRINT)
                            {
                                response.KagitNusha = true;
                                response.Mesaj = "e-Arşiv Fatura başarıyla gönderildi. \nEvrak No: " + Result.EvrakNo + "\nYazdırmak İster Misiniz?";
                                response.Dosya = ByteData;
                            }
                            else
                            {
                                response.KagitNusha = false;
                                response.Mesaj = "e-Arşiv Fatura başarıyla gönderildi. \nEvrak No: " + Result.EvrakNo;
                                response.Dosya = null;
                            }

                            return response;
                        default:
                            throw new Exception("Tanımlı Entegratör Bulunamadı!");
                    }
                }
                return response;
            }
            public static string TopluGonder(List<InvoiceType> Faturalar, List<string> Subeler, bool FarkliSube)
            {
                StringBuilder sb = new StringBuilder();
                var Result = new Results.EFAGDN();

                UBLBaseSerializer ser = new InvoiceSerializer();
                if (Connector.m.SchematronKontrol)
                {
                    foreach (var Fatura in Faturalar)
                    {
                        var schematronResult = SchematronChecker.Check(Fatura, SchematronDocType.eArsiv);
                        if (schematronResult.SchemaResult != "Başarılı" || schematronResult.SchematronResult != "Başarılı")
                            throw new Exception(schematronResult.Detail);
                    }
                }
                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var fitEArsiv = new FIT.ArchiveWebService();
                        fitEArsiv.WebServisAdresDegistir();
                        //Connector.m.PkEtiketi = PKETIKET;
                        if (!FarkliSube)
                        {
                            var fitResult = fitEArsiv.TopluEArsivGonder(Faturalar, Subeler[0]);

                            foreach (var a in fitResult.preCheckSuccessResults)
                            {
                                foreach (var doc in Faturalar)
                                {
                                    if (doc.UUID.Value == a.UUID)
                                    {
                                        doc.ID.Value = a.InvoiceNumber;
                                        sb.AppendLine("e-Arşiv Fatura başarıyla gönderildi. \nEvrak No: " + a.InvoiceNumber);
                                        doc.ID.Value = a.InvoiceNumber;

                                        Result.DurumAciklama = a.SuccessDesc;
                                        Result.DurumKod = a.SuccessCode + "";
                                        Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                                        Result.EvrakNo = a.InvoiceNumber;
                                        Result.UUID = a.UUID;
                                        Result.ZarfUUID = "";
                                        Result.YanitDurum = 0;

                                        Entegrasyon.UpdateEfagdn(Result, Convert.ToInt32(doc.AdditionalDocumentReference.Where(element => element.DocumentTypeCode?.Value == "CUST_INV_ID").First().ID.Value), ser.GetXmlAsByteArray(doc));
                                        break;
                                    }
                                }
                            }
                            foreach (var b in fitResult.preCheckErrorResults)
                            {
                                sb.AppendLine(String.Format("Hatalı e-Arşiv Fatura:{0} - Hata:{1}({2})", b.InvoiceNumber, b.ErrorDesc, b.ErrorCode));
                            }
                            if (fitResult.Result.Result1 == ResultType.FAIL)
                                sb.AppendLine(fitResult.Detail);
                        }
                        else
                        {
                            int i = 0;
                            foreach (var fatura in Faturalar)
                            {
                                UBLBaseSerializer serializer = new InvoiceSerializer();  // UBL  XML e dönüştürülür
                                var strFatura = serializer.GetXmlAsString(fatura); // XML byte tipinden string tipine dönüştürülür

                                var fitResult = fitEArsiv.EArsivGonder(strFatura, fatura.UUID.Value, Subeler[i]);
                                i++;

                                Connector.m.FaturaUUID = fitResult.preCheckSuccessResults[0].UUID;
                                Connector.m.FaturaID = fitResult.preCheckSuccessResults[0].InvoiceNumber;
                                //var fitFaturaUBL = fitEArsiv.FaturaUBLIndir();
                                //ByteData = fitFaturaUBL.binaryData;

                                Result.DurumAciklama = fitResult.preCheckSuccessResults[0].SuccessDesc;
                                Result.DurumKod = fitResult.preCheckSuccessResults[0].SuccessCode + "";
                                Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                                Result.EvrakNo = fitResult.preCheckSuccessResults[0].InvoiceNumber;
                                Result.UUID = fitResult.preCheckSuccessResults[0].UUID;
                                Result.ZarfUUID = "";
                                Result.YanitDurum = 0;
                                sb.AppendLine("e-Arşiv Fatura başarıyla gönderildi. \nEvrak No: " + Result.EvrakNo);

                                Entegrasyon.UpdateEfagdn(Result, Convert.ToInt32(fatura.AdditionalDocumentReference.Where(element => element.DocumentTypeCode.Value == "CUST_INV_ID").First().ID.Value), null);
                                if (Connector.m.DokumanIndir)
                                {
                                    var Gonderilen = fitEArsiv.ImzaliIndir(Result.UUID, "", 0);
                                    Entegrasyon.UpdateEfados(Gonderilen.binaryData);
                                }
                            }
                        }
                        return sb.ToString();
                    case "DPLANET":
                        var dpEArsiv = new DigitalPlanet.ArchiveWebService();

                        foreach (var fatura in Faturalar)
                        {
                            int EVRAKSN = Convert.ToInt32(fatura.AdditionalDocumentReference.FirstOrDefault(elm => elm.DocumentTypeCode.Value == "CUST_INV_ID")?.ID.Value ?? "0");
                            fatura.ID = fatura.ID ?? new UblInvoiceObject.IDType { Value = "CPM" + DateTime.Now.Year + EVRAKSN.ToString("000000000") };
                            //fatura.ID = fatura.ID ?? new UblInvoiceObject.IDType { Value = "GIB2022000000001 " };
                            UBLBaseSerializer serializer = new InvoiceSerializer();  // UBL  XML e dönüştürülür
                            var strFatura = serializer.GetXmlAsString(fatura); // XML byte tipinden string tipine dönüştürülür

                            dpEArsiv.WebServisAdresDegistir();
                            var dpResult = dpEArsiv.EArsivGonder(strFatura, fatura.IssueDate.Value);

                            if (dpResult.ServiceResult == COMMON.dpInvoice.Result.Error)
                            {
                                Connector.m.Hata = true;
                                sb.AppendLine(dpResult.ServiceResultDescription);
                                return sb.ToString();
                            }
                            else
                            {
                                foreach (var a in dpResult.Invoices)
                                {
                                    foreach (var doc in Faturalar)
                                    {
                                        if (doc.UUID.Value == a.UUID)
                                        {
                                            sb.AppendLine("e-Arşiv Fatura başarıyla gönderildi. \nEvrak No: " + a.InvoiceId);

                                            Result.DurumAciklama = a.StatusDescription;
                                            Result.DurumKod = a.StatusCode + "";
                                            Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                                            Result.EvrakNo = a.InvoiceId;
                                            Result.UUID = a.UUID;
                                            Result.ZarfUUID = "";
                                            Result.YanitDurum = 0;

                                            Entegrasyon.UpdateEfagdn(Result, Convert.ToInt32(doc.AdditionalDocumentReference.Where(element => element.DocumentTypeCode.Value == "CUST_INV_ID").First().ID.Value), ser.GetXmlAsByteArray(doc));
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        return sb.ToString();
                    case "EDM":
                        var edmEArsiv = new EDM.ArchiveWebService();
                        edmEArsiv.WebServisAdresDegistir();
                        var edmResult = edmEArsiv.TopluEArsivGonder(Faturalar);

                        foreach (var a in edmResult.INVOICE)
                        {
                            foreach (var doc in Faturalar)
                            {
                                if (doc.UUID.Value == a.UUID)
                                {
                                    sb.AppendLine("e-Arşiv Fatura başarıyla gönderildi. \nEvrak No: " + a.ID);

                                    Result.DurumAciklama = a.HEADER.STATUS_DESCRIPTION;
                                    Result.DurumKod = "";
                                    Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                                    Result.EvrakNo = a.ID;
                                    Result.UUID = a.UUID;
                                    Result.ZarfUUID = a.HEADER.ENVELOPE_IDENTIFIER ?? "";
                                    Result.YanitDurum = 0;

                                    Entegrasyon.UpdateEfagdn(Result, Convert.ToInt32(doc.AdditionalDocumentReference.Where(element => element.DocumentTypeCode.Value == "CUST_INV_ID").First().ID.Value), ser.GetXmlAsByteArray(doc));
                                    break;
                                }
                            }
                        }
                        return sb.ToString();
                    case "QEF":
                        var qefEArsiv = new QEF.ArchiveService();

                        foreach (var fatura in Faturalar)
                        {
                            if (fatura.Note == null)
                                fatura.Note = new List<UblInvoiceObject.NoteType>().ToArray();

                            var noteList = fatura.Note.ToList();
                            noteList.Add(new UblInvoiceObject.NoteType { Value = "Gönderim Şekli: ELEKTRONIK" });

                            fatura.Note = noteList.ToArray();

                            UBLBaseSerializer serializer = new InvoiceSerializer();  // UBL  XML e dönüştürülür
                            var strFatura = serializer.GetXmlAsString(fatura); // XML byte tipinden string tipine dönüştürülür

                            var sube = "DFLT";

                            if (Subeler.Count > 0)
                                sube = Subeler[0];

                            sube = sube == "default" ? "DFLT" : sube;
                            var EVRAKSN = Convert.ToInt32(fatura.AdditionalDocumentReference.Where(element => element.DocumentTypeCode.Value == "CUST_INV_ID").First().ID.Value);
                            var qefResult = qefEArsiv.EArsivGonder(strFatura, Connector.m.VknTckn, EVRAKSN, sube, fatura.IssueDate.Value.Date);

                            if (qefResult.Result.resultText != "İşlem başarılı.")
                                throw new Exception(qefResult.Result.resultText);

                            var ByteData = qefResult.Belge.belgeIcerigi;

                            Result.DurumAciklama = qefResult.Result.resultText;
                            Result.DurumKod = qefResult.Result.resultCode;
                            Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                            Result.EvrakNo = qefResult.Result.resultExtra.First(elm => elm.key.ToString() == "faturaNo").value.ToString();
                            Result.UUID = qefResult.Result.resultExtra.First(elm => elm.key.ToString() == "uuid").value.ToString();
                            Result.ZarfUUID = "";
                            Result.YanitDurum = 0;

                            Entegrasyon.UpdateEfagdn(Result, EVRAKSN, ByteData);

                            sb.AppendLine("e-Arşiv Fatura başarıyla gönderildi. \nEvrak No: " + Result.EvrakNo);
                        }
                        return sb.ToString();
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
            public static string Iptal(int EVRAKSN, string EVRAKNO, string GUID, decimal TUTAR)
            {
                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var fitEArsiv = new FIT.ArchiveWebService();
                        var fitResult = fitEArsiv.EArsivIptal(EVRAKNO, TUTAR);
                        if (fitResult.Result.Result1 == ResultType.SUCCESS)
                        {
                            Entegrasyon.SilEarsiv(EVRAKSN, TUTAR, fitResult.invoiceCancellation.message, DateTime.Now);
                        }
                        else
                        {
                            throw new Exception(fitResult.invoiceCancellation.message);
                        }
                        return fitResult.invoiceCancellation.message + " ve İlgili eArşiv Fatura Silinmiştir!";
                    case "DPLANET":
                        var dpEArsiv = new DigitalPlanet.ArchiveWebService();
                        var dpResult = dpEArsiv.IptalFatura(GUID, TUTAR);
                        if (dpResult.ServiceResult == COMMON.dpInvoice.Result.Error)
                            throw new Exception(dpResult.ServiceResultDescription);
                        else
                            Entegrasyon.SilEarsiv(EVRAKSN, TUTAR, dpResult.StatusDescription, DateTime.Now);

                        return "eArşiv Fatura Başarıyla İptal Edilmiştir ve İlgili eArşiv Fatura Silinmiştir!";
                    case "EDM":
                        var edmEArsiv = new EDM.ArchiveWebService();
                        var edmpResult = edmEArsiv.IptalFatura(GUID, TUTAR);

                        Entegrasyon.SilEarsiv(EVRAKSN, TUTAR, "", DateTime.Now);

                        return "eArşiv Fatura Başarıyla İptal Edilmiştir ve İlgili eArşiv Fatura Silinmiştir!";
                    case "QEF":
                        var qefEArsiv = new QEF.ArchiveService();
                        var qefpResult = qefEArsiv.IptalFatura(GUID);

                        if (qefpResult.resultText != "İşlem başarılı.")
                            throw new Exception(qefpResult.resultText);

                        Entegrasyon.SilEarsiv(EVRAKSN, TUTAR, qefpResult.resultText, DateTime.Now);

                        return "eArşiv Fatura Başarıyla İptal Edilmiştir ve İlgili eArşiv Fatura Silinmiştir!";
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
            public static string Itiraz(string EVRAKNO, UBL.ObjectionClass Objection)
            {
                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var fitEArsiv = new FIT.ArchiveWebService();
                        var fitResult = fitEArsiv.EArsivItiraz(EVRAKNO, Objection);
                        if (fitResult.Result == ResultType.FAIL)
                        {
                            throw new Exception(fitResult.Detail);
                        }
                        return fitResult.Detail;
                    case "DPLANET":
                        var dpArsiv = new DigitalPlanet.ArchiveWebService();
                        dpArsiv.WebServisAdresDegistir();
                        dpArsiv.EArsivItiraz();
                        return "Test!";
                    case "EDM":
                        var edmArsiv = new EDM.ArchiveWebService();
                        edmArsiv.WebServisAdresDegistir();
                        edmArsiv.EArsivItiraz();
                        return "Test!";
                    case "QEF":
                        //var edmArsiv = new EDM.ArchiveWebService();
                        //edmArsiv.WebServisAdresDegistir();
                        //edmArsiv.EArsivItiraz();
                        return "Test!";
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
            public static void GonderilenGuncelle(int EVRAKSN)
            {
                var Result = new Results.EFAGDN();
                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var fitArsiv = new FIT.ArchiveWebService();
                        fitArsiv.WebServisAdresDegistir();

                        string UUID = Entegrasyon.GetUUIDFromEvraksn(new List<int> { EVRAKSN })[0];
                        var evrakno = Entegrasyon.GetEvraknoFromEvraksn(new[] { EVRAKSN }.ToList())[0];

                        getSignedInvoiceResponseType ars = null;
                        Exception exx = null;

                        try
                        {
                            ars = fitArsiv.ImzaliIndir(UUID.ToUpper(), evrakno, EVRAKSN);
                        }
                        catch (Exception ex)
                        {
                            exx = ex;
                        }
                        try
                        {
                            ars = fitArsiv.ImzaliIndir(UUID.ToLower(), evrakno, EVRAKSN);
                        }
                        catch (Exception ex)
                        {
                            exx = ex;
                        }

                        if (ars != null)
                        {
                            Result.DurumAciklama = ars.Detail;
                            Result.DurumKod = "150";
                            Result.DurumZaman = DateTime.Now;
                            Result.EvrakNo = ars.invoiceNumber;
                            Result.UUID = ars.UUID;
                            Result.ZarfUUID = "";
                            Result.YanitDurum = 0;

                            Entegrasyon.UpdateEfagdn(Result, EVRAKSN, ars.binaryData, onlyUpdate: true);
                        }
                        else if (exx != null)
                        {
                            throw exx;
                            //Değiştir--CpmMessageBox.Show($"Entegratörden Fatura Bilgisi Dönmedi.\nEvraksn:{EVRAKSN}\nEvrakno:{evrakno}\nUUID:{UUID}", "Dikkat", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                            //Değiştir--CpmMessageBox.Show($"Entegratör Açıklaması: {exx.Message}", "Dikkat", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                        }

                        break;
                    case "DPLANET":
                        var dpArsiv = new DigitalPlanet.ArchiveWebService();
                        dpArsiv.WebServisAdresDegistir();

                        var uuid = Entegrasyon.GetUUIDFromEvraksn(new[] { EVRAKSN }.ToList());

                        if (uuid[0] != "")
                        {
                            var dpArs = dpArsiv.EArsivIndir(uuid[0]);
                            XmlSerializer ser = new XmlSerializer(typeof(InvoiceType));

                            var byteData = dpArsiv.EArsivIndir(dpArs.UUID).ReturnValue;
                            //File.WriteAllBytes("test.xml", byteData);
                            var i = (InvoiceType)ser.Deserialize(new MemoryStream(byteData));
                            if (i.AdditionalDocumentReference.First(elm => elm.DocumentTypeCode.Value == "CUST_INV_ID").ID.Value == EVRAKSN + "")
                            {
                                Result.DurumAciklama = dpArs.StatusDescription;
                                Result.DurumKod = dpArs.StatusCode + "";
                                Result.DurumZaman = DateTime.Now;
                                Result.EvrakNo = dpArs.InvoiceId;
                                Result.UUID = dpArs.UUID;
                                Result.ZarfUUID = "";
                                Result.YanitDurum = 0;

                                Entegrasyon.UpdateEfagdn(Result, EVRAKSN, byteData, true);
                            }
                        }
                        else
                        {
                            var dt = Entegrasyon.GetGonderimZaman(new[] { EVRAKSN }.ToList())[0];

                            if (dt == new DateTime(1900, 1, 1))
                                dt = DateTime.Now;

                            var start = new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0);
                            var end = new DateTime(dt.Year, dt.Month, dt.Day, 23, 59, 59);

                            var invoices = dpArsiv.EArsivIndir(start, end);

                            XmlSerializer ser = new XmlSerializer(typeof(InvoiceType));

                            foreach (var inv in invoices.Invoices)
                            {
                                var byteData = dpArsiv.EArsivIndir(inv.UUID).ReturnValue;
                                //File.WriteAllBytes("test.xml", byteData);
                                var i = (InvoiceType)ser.Deserialize(new MemoryStream(byteData));
                                if (i.AdditionalDocumentReference.First(elm => elm.DocumentTypeCode.Value == "CUST_INV_ID").ID.Value == EVRAKSN + "")
                                {
                                    Result.DurumAciklama = inv.StatusDescription;
                                    Result.DurumKod = inv.StatusCode + "";
                                    Result.DurumZaman = DateTime.Now;
                                    Result.EvrakNo = inv.InvoiceId;
                                    Result.UUID = inv.UUID;
                                    Result.ZarfUUID = "";
                                    Result.YanitDurum = 0;

                                    Entegrasyon.UpdateEfagdn(Result, EVRAKSN, byteData, true);
                                }
                            }
                        }
                        break;
                    case "EDM":
                        var edmArsiv = new EDM.ArchiveWebService();
                        var edmGelen = edmArsiv.EArsivIndir(Entegrasyon.GetUUIDFromEvraksn(new List<int> { EVRAKSN })[0]);

                        if (edmGelen.Length > 0)
                        {
                            Result.DurumAciklama = edmGelen[0].HEADER.STATUS_DESCRIPTION;
                            Result.DurumKod = "";
                            Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                            Result.EvrakNo = edmGelen[0].ID;
                            Result.UUID = edmGelen[0].UUID;
                            Result.ZarfUUID = edmGelen[0].HEADER.ENVELOPE_IDENTIFIER ?? "";
                            Result.YanitDurum = 0;

                            Entegrasyon.UpdateEfagdn(Result, EVRAKSN, edmGelen[0].CONTENT.Value, onlyUpdate: true);

                            Result.DurumAciklama = edmGelen[0].HEADER.RESPONSE_CODE == "REJECT" ? "" : "";
                            Result.DurumKod = edmGelen[0].HEADER.RESPONSE_CODE == "REJECT" ? "3" : (edmGelen[0].HEADER.RESPONSE_CODE == "ACCEPT" ? "2" : "");
                            Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                            Result.UUID = edmGelen[0].UUID;

                            Entegrasyon.UpdateEfagdnStatus(Result);

                            //File.WriteAllText("edm.json", JsonConvert.SerializeObject(edmGelen));

                            if (edmGelen[0].HEADER.EARCHIVE_REPORT_UUID != null)
                                Entegrasyon.UpdateEfagdnGonderimDurum(edmGelen[0].UUID, 4);
                            else if (edmGelen[0].HEADER.STATUS_DESCRIPTION == "SUCCEED")
                                Entegrasyon.UpdateEfagdnGonderimDurum(edmGelen[0].UUID, 3);
                        }
                        else
                            throw new Exception(EVRAKSN + " Seri Numaralı Evrak Gönderilenler Listesinde Bulunmamaktadır!");
                        break;
                    case "QEF":
                        var qefArsiv = new QEF.ArchiveService();
                        var qefGelen = qefArsiv.EArsivIndir(Entegrasyon.GetUUIDFromEvraksn(new List<int> { EVRAKSN })[0]);

                        Result.DurumAciklama = qefGelen.Result.resultText;
                        Result.DurumKod = qefGelen.Result.resultCode;
                        Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                        Result.EvrakNo = qefGelen.Result.resultExtra.First(elm => elm.key.ToString() == "faturaNo").value.ToString();
                        Result.UUID = qefGelen.Result.resultExtra.First(elm => elm.key.ToString() == "uuid").value.ToString();
                        Result.ZarfUUID = "";
                        Result.YanitDurum = 0;

                        Entegrasyon.UpdateEfagdn(Result, EVRAKSN, qefGelen.Belge.belgeIcerigi, onlyUpdate: true);

                        //if (qefGelen[0].HEADER.EARCHIVE_REPORT_UUID != null)
                        //    Entegrasyon.UpdateEfagdnGonderimDurum(qefGelen[0].UUID, 4);
                        //else if (qefGelen[0].HEADER.STATUS_DESCRIPTION == "SUCCEED")
                        //    Entegrasyon.UpdateEfagdnGonderimDurum(qefGelen[0].UUID, 3);
                        //else
                        //    throw new Exception(EVRAKSN + " Seri Numaralı Evrak Gönderilenler Listesinde Bulunmamaktadır!");
                        break;
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
        }
        public static class EIrsaliye
        {
            public static string Gonder(int EVRAKSN)
            {
                DespatchAdvice despatchAdvice = new DespatchAdvice();
                var doc = despatchAdvice.CreateDespactAdvice(EVRAKSN);

                if (UrlModel.SelectedItem == "QEF" && string.IsNullOrEmpty(doc.ID.Value) && Connector.m.SablonTip)
                {
                    var sablon = string.IsNullOrEmpty(despatchAdvice.ENTSABLON) ? Connector.m.Sablon : despatchAdvice.ENTSABLON;
                    var notes = doc.Note.ToList();
                    notes.Add(new UblDespatchAdvice.NoteType { Value = $"#EFN_SERINO_TERCIHI#{sablon}#" });

                    doc.Note = notes.ToArray();
                }

                if (Connector.m.SchematronKontrol)
                {
                    var schematronResult = SchematronChecker.Check(doc, SchematronDocType.eIrsaliye);
                    if (schematronResult.SchemaResult != "Başarılı" || schematronResult.SchematronResult != "Başarılı")
                        throw new Exception(schematronResult.Detail);
                }

                UBLBaseSerializer serializer = new DespatchAdviceSerializer();
                var strFatura = serializer.GetXmlAsString(doc);

                Connector.m.PkEtiketi = despatchAdvice.PK;
                Entegrasyon.EfagdnUuid(EVRAKSN, doc.UUID.Value);


                var Result = new Results.EFAGDN();

                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var fitIrsaliye = new FIT.DespatchWebService();
                        fitIrsaliye.WebServisAdresDegistir();

                        var fitResult = fitIrsaliye.IrsaliyeGonder(strFatura, doc.UUID.Value);

                        //var fitFaturaUBL = fitIrsaliye.GonderilenIrsaliyeUBLIndir(new[] { fitResult.Response[0].UUID });
                        //var faturaBytes = ZipUtility.UncompressFile(fitFaturaUBL.Response[0].DocData);

                        Result.DurumAciklama = "";
                        Result.DurumKod = "1";
                        Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                        Result.EvrakNo = fitResult.Response[0].ID;
                        Result.UUID = fitResult.Response[0].UUID;
                        Result.ZarfUUID = fitResult.Response[0].EnvUUID;

                        //Entegrasyon.UpdateEirsaliye(Result, EVRAKSN, faturaBytes);
                        Entegrasyon.UpdateEirsaliye(Result, EVRAKSN, null);
                        if (Connector.m.DokumanIndir)
                        {
                            var Gonderilen = fitIrsaliye.GonderilenIrsaliyeUBLIndir(new[] { Result.UUID });
                            Entegrasyon.UpdateEfadosIrsaliye(ZipUtility.UncompressFile(Gonderilen.Response[0].DocData));
                        }
                        return "e-İrsaliye başarıyla gönderildi. \nEvrak No: " + fitResult.Response[0].ID;
                    case "DPLANET":
                        var dpIrsaliye = new DigitalPlanet.DespatchWebService();
                        dpIrsaliye.Login();

                        var dpResult = dpIrsaliye.EIrsaliyeGonder(strFatura, doc.UUID.Value, doc.IssueDate.Value, despatchAdvice.ENTSABLON);

                        if (dpResult.ServiceResult == COMMON.dpDespatch.Result.Error)
                            throw new Exception(dpResult.ErrorCode + ":" + dpResult.ServiceResultDescription);

                        //var dpFaturaUBL = dpIrsaliye.GonderilenEIrsaliyeIndir(dpResult.Despatches[0].UUID);

                        Result.DurumAciklama = dpResult.ServiceResultDescription;
                        Result.DurumKod = dpResult.ServiceResult == COMMON.dpDespatch.Result.Successful ? "1" : dpResult.ErrorCode.ToString();
                        Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                        Result.EvrakNo = dpResult.Despatches[0].DespatchId;
                        Result.UUID = dpResult.Despatches[0].UUID;
                        Result.ZarfUUID = dpResult.InstanceIdentifier;

                        //Entegrasyon.UpdateEirsaliye(Result, EVRAKSN, dpFaturaUBL.Despatches[0].ReturnValue);
                        Entegrasyon.UpdateEirsaliye(Result, EVRAKSN, null);
                        if (Connector.m.DokumanIndir)
                        {
                            var Gonderilen = dpIrsaliye.GonderilenEIrsaliyeIndir(Result.UUID);
                            Entegrasyon.UpdateEfadosIrsaliye(Gonderilen.Despatches[0].ReturnValue);
                        }
                        return "e-İrsaliye başarıyla gönderildi. \nEvrak No: " + dpResult.Despatches[0].DespatchId;
                    case "EDM":
                        var edmIrsaliye = new EDM.DespatchWebService();
                        edmIrsaliye.Login();

                        var edmResult = edmIrsaliye.EIrsaliyeGonder(strFatura, doc.DeliveryCustomerParty.Party.PartyIdentification[0].ID.Value, Connector.m.VknTckn); ;

                        var edmFaturaUBL = edmIrsaliye.GonderilenEIrsaliyeIndir(edmResult.DESPATCH[0].UUID);

                        Result.DurumAciklama = edmFaturaUBL[0].HEADER.STATUS_DESCRIPTION;
                        Result.DurumKod = "1";
                        Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                        Result.EvrakNo = edmFaturaUBL[0].ID;
                        Result.UUID = edmFaturaUBL[0].UUID;
                        Result.ZarfUUID = edmFaturaUBL[0].HEADER.ENVELOPE_IDENTIFIER ?? "";

                        Entegrasyon.UpdateEirsaliye(Result, EVRAKSN, edmFaturaUBL[0].CONTENT.Value);
                        return "e-İrsaliye başarıyla gönderildi. \nEvrak No: " + edmFaturaUBL[0].ID;
                    case "QEF":
                        var qefIrsaliye = new QEF.DespatchAdviceService();

                        var qefResult = qefIrsaliye.IrsaliyeGonder(strFatura, EVRAKSN, doc.IssueDate.Value.Date);

                        if (!string.IsNullOrEmpty(qefResult.aciklama))
                            throw new Exception(qefResult.aciklama);

                        var qefFaturaUBL = qefIrsaliye.IrsaliyeUBLIndir(new[] { doc.UUID.Value });

                        Result.DurumAciklama = qefResult.aciklama ?? "";
                        Result.DurumKod = qefResult.gonderimDurumu.ToString();
                        Result.DurumZaman = DateTime.TryParse(qefResult.gonderimTarihi, out DateTime dz) ? dz : new DateTime(1900, 1, 1);
                        Result.EvrakNo = qefResult.belgeNo;
                        Result.UUID = qefResult.ettn;
                        Result.ZarfUUID = "";

                        Entegrasyon.UpdateEirsaliye(Result, EVRAKSN, qefFaturaUBL.First().Value);
                        return "e-İrsaliye başarıyla gönderildi. \nEvrak No: " + Result.EvrakNo;
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
            public static List<AlinanBelge> AlinanFaturalarListesi(DateTime StartDate, DateTime EndDate)
            {
                var data = new List<AlinanBelge>();
                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var fitIrsaliye = new FIT.DespatchWebService();
                        fitIrsaliye.WebServisAdresDegistir();

                        for (DateTime dt = StartDate.Date; dt < EndDate.Date.AddDays(1); dt = dt.AddDays(1))
                        {
                            var fitResult = fitIrsaliye.GonderilenIrsaliyeler(dt);

                            foreach (var irsaliye in fitResult)
                            {
                                data.Add(new AlinanBelge
                                {
                                    EVRAKGUID = Guid.Parse(irsaliye.UUID),
                                    EVRAKNO = irsaliye.ID,
                                    YUKLEMEZAMAN = irsaliye.InsertDateTime,
                                    GBETIKET = "",
                                    GBUNVAN = ""
                                });
                            }
                        }
                        break;
                    case "DPLANET":
                        var dpIrsaliye = new DigitalPlanet.DespatchWebService();
                        dpIrsaliye.WebServisAdresDegistir();
                        dpIrsaliye.Login();

                        Connector.m.IssueDate = StartDate;
                        Connector.m.EndDate = EndDate;

                        foreach (var irsaliye in dpIrsaliye.GelenEIrsaliyeler().Despatches)
                        {
                            if (irsaliye.Issuetime > StartDate)
                                data.Add(new AlinanBelge
                                {
                                    EVRAKGUID = Guid.Parse(irsaliye.UUID),
                                    EVRAKNO = irsaliye.DespatchId,
                                    YUKLEMEZAMAN = irsaliye.Issuetime,
                                    GBETIKET = irsaliye.SenderPostBoxName,
                                    GBUNVAN = irsaliye.Partyname
                                });
                        }
                        break;
                    case "EDM":
                        break;
                    case "QEF":
                        var qefIrsaliye = new QEF.GetDespatchAdviceService();

                        foreach (var fatura in qefIrsaliye.GelenEIrsaliyeler(StartDate, EndDate))
                        {
                            data.Add(new AlinanBelge
                            {
                                EVRAKGUID = Guid.Parse(fatura.Value.ettn),
                                EVRAKNO = fatura.Value.ettn,
                                YUKLEMEZAMAN = DateTime.ParseExact(fatura.Value.alimTarihi, "yyyyMMdd", CultureInfo.CurrentCulture),
                                GBETIKET = "",
                                GBUNVAN = ""
                            });
                        }
                        break;
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
                return data;
            }
            public static void YanitGonder(ReceiptAdviceType Yanit)
            {
                ReceiptAdviceSerializer serializer = new ReceiptAdviceSerializer();
                var docStr = serializer.GetXmlAsString(Yanit);
                var doc = Encoding.UTF8.GetBytes(docStr);
                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var fitIrsaliye = new FIT.DespatchWebService();
                        fitIrsaliye.WebServisAdresDegistir();

                        //var res = fitIrsaliye.IrsaliyeYanitiGonder(Yanit);
                        //var fitDosya = fitIrsaliye.IrsaliyeYanitiIndir(res.Response[0].UUID);
                        //var fitYnt = Entegrasyon.ConvertToYanit(fitDosya.Response[0], "GDN");
                        //
                        //Entegrasyon.InsertIntoEirYnt(fitYnt);

                        Yanit.ID = new UblReceiptAdvice.IDType { Value = Entegrasyon.GetIrsaliyeYanitEvrakNo() };

                        var res = fitIrsaliye.IrsaliyeYanitiGonder(Yanit);
                        var fitDosya = fitIrsaliye.GonderilenIrsaliyeYanitlari(res.Response[0].EnvUUID, res.Response[0].UUID);
                        var fitYnt = Entegrasyon.ConvertToYanit(fitDosya, "GDN", res.Response[0].EnvUUID);
                        Entegrasyon.InsertIntoEirYnt(fitYnt);
                        break;
                    case "DPLANET":
                        var dpIrsaliye = new DigitalPlanet.DespatchWebService();
                        dpIrsaliye.WebServisAdresDegistir();
                        dpIrsaliye.Login();

                        var dpSonuc = dpIrsaliye.EIrsaliyeCevap(doc);
                        //File.WriteAllText("ReceiptAdvice_Resp.json", JsonConvert.SerializeObject(dpSonuc));
                        if (dpSonuc.ServiceResult == COMMON.dpDespatch.Result.Error)
                            throw new Exception(dpSonuc.ServiceResultDescription);

                        foreach (var receipments in dpSonuc.Receipments)
                        {
                            var irsaliyeYanit = dpIrsaliye.GidenEIrsaliyeYanitIndir(receipments.UUID);
                            var ynt = Entegrasyon.ConvertToYanit(irsaliyeYanit.Receipments[0], "GDN");

                            Entegrasyon.InsertIntoEirYnt(ynt);
                        }
                        break;
                    case "EDM":
                        break;
                    case "QEF":
                        break;
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
            public static void YanitGuncelle(string UUID, string ZarfGuid)
            {
                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var fitIrsaliye = new FIT.DespatchWebService();
                        fitIrsaliye.WebServisAdresDegistir();
                        var fitDosya = fitIrsaliye.GonderilenIrsaliyeYanitlari(ZarfGuid, UUID);
                        var fitYnt = Entegrasyon.ConvertToYanit(fitDosya, "GDN", ZarfGuid);

                        if (appConfig.Debugging)
                        {
                            MessageBox.Show(ZarfGuid);
                        }
                        Entegrasyon.UpdateEirYnt(fitYnt);
                        break;
                    case "DPLANET":
                        var dpIrsaliye = new DigitalPlanet.DespatchWebService();
                        dpIrsaliye.WebServisAdresDegistir();
                        dpIrsaliye.Login();

                        var dpSonuc = dpIrsaliye.EIrsaliyeYanitDurumu(UUID);
                        var durum = new EIRYNT
                        {
                            DURUMACIKLAMA = dpSonuc.StatusDescription,
                            DURUMKOD = dpSonuc.StatusCode == 54 ? "1300" : dpSonuc.StatusCode + "",
                            EVRAKGUID = UUID
                        };

                        Entegrasyon.UpdateEirYnt(durum);
                        break;
                    case "EDM":
                        break;
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
            public static void GonderilenYanitlar(string UUID)
            {
                switch (UrlModel.SelectedItem)
                {
                    case "ING":
                    case "INGBANK":
                    case "FIT":
                        var fitIrsaliye = new FIT.DespatchWebService();
                        fitIrsaliye.WebServisAdresDegistir();
                        var yanitlar = fitIrsaliye.IrsaliyeYanitiIndir(UUID);

                        var yanitlar2 = Entegrasyon.ConvertToYanitList(yanitlar.Response[0], "GDN", Entegrasyon.GetEvrakNoFromGuid(UUID));
                        if (appConfig.Debugging)
                        {
                            if (yanitlar?.Response?.Length > 0)
                            {
                                if (yanitlar?.Response[0]?.Receipts?.Length > 0)
                                {
                                    MessageBox.Show(yanitlar.Response[0].Receipts[0].EnvUUID);
                                }
                            }
                        }
                        foreach (var yanit in yanitlar2)
                        {
                            Entegrasyon.InsertIntoEirYnt(yanit);
                            Entegrasyon.UpdateEirYnt(yanit);
                        }
                        break;
                    case "DPLANET":
                        //var dpIrsaliye = new DigitalPlanet.DespatchWebService();
                        //dpIrsaliye.EIrsaliyeGonderilenYanitlar(UUID);
                        break;
                    case "EDM":
                        break;
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
            public static string TopluGonder(string PK, List<DespatchAdviceType> Irsaliyeler, string ENTSABLON)
            {
                StringBuilder sb = new StringBuilder();
                var Result = new Results.EFAGDN();


                if (Connector.m.SchematronKontrol)
                {
                    DespatchAdviceSerializer ser = new DespatchAdviceSerializer();
                    foreach (var Irsaliye in Irsaliyeler)
                    {
                        var schematronResult = SchematronChecker.Check(Irsaliye, SchematronDocType.eIrsaliye);
                        if (schematronResult.SchemaResult != "Başarılı" || schematronResult.SchematronResult != "Başarılı")
                            throw new Exception(schematronResult.Detail);
                    }
                }

                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var fitIrsaliye = new FIT.DespatchWebService();
                        fitIrsaliye.WebServisAdresDegistir();
                        Connector.m.PkEtiketi = PK;
                        var fitResult = fitIrsaliye.TopluIrsaliyeGonder(Irsaliyeler);
                        var fitEnvResult = fitIrsaliye.ZarfDurumSorgula(new[] { fitResult.Response[0].EnvUUID });

                        foreach (var doc in fitResult.Response)
                        {
                            //var fat = fitIrsaliye.GonderilenIrsaliyeUBLIndir(new[] { doc.UUID });
                            Result.DurumAciklama = fitEnvResult[0].Description;
                            Result.DurumKod = fitEnvResult[0].ResponseCode;
                            Result.DurumZaman = fitEnvResult[0].IssueDate;
                            Result.EvrakNo = doc.ID;
                            Result.UUID = doc.UUID;
                            Result.ZarfUUID = doc.EnvUUID;

                            //Entegrasyon.UpdateEirsaliye(Result, Convert.ToInt32(doc.CustDesID), fat.Response[0].DocData);
                            Entegrasyon.UpdateEirsaliye(Result, Convert.ToInt32(doc.CustDesID), null);
                            if (Connector.m.DokumanIndir)
                            {
                                var Gonderilen = fitIrsaliye.GonderilenIrsaliyeUBLIndir(new[] { Result.UUID });
                                Entegrasyon.UpdateEfadosIrsaliye(Gonderilen.Response[0].DocData);
                            }
                            sb.AppendLine("e-İrsaliye başarıyla gönderildi. \nEvrak No: " + Result.EvrakNo);
                        }
                        return sb.ToString();
                    case "DPLANET":
                        var dpIrsaliye = new DigitalPlanet.DespatchWebService();
                        dpIrsaliye.WebServisAdresDegistir();
                        dpIrsaliye.Login();
                        Connector.m.PkEtiketi = PK;
                        var dpResult = dpIrsaliye.TopluEIrsaliyeGonder(Irsaliyeler, ENTSABLON);

                        if (dpResult.ServiceResult == COMMON.dpDespatch.Result.Error)
                            throw new Exception(dpResult.ServiceResultDescription);

                        foreach (var doc in dpResult.Despatches)
                        {
                            var fat = dpIrsaliye.GonderilenEIrsaliyeIndir(doc.UUID);
                            XmlSerializer serializer = new XmlSerializer(typeof(DespatchAdviceType));
                            DespatchAdviceType inv = (DespatchAdviceType)serializer.Deserialize(new MemoryStream(fat.Despatches[0].ReturnValue));

                            Result.DurumAciklama = fat.ServiceResultDescription;
                            Result.DurumKod = fat.ServiceResult == COMMON.dpDespatch.Result.Successful ? "1" : dpResult.ErrorCode + "";
                            Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                            Result.EvrakNo = fat.Despatches[0].DespatchId;
                            Result.UUID = fat.Despatches[0].UUID;
                            Result.ZarfUUID = dpResult.InstanceIdentifier;

                            Entegrasyon.UpdateEirsaliye(Result, Convert.ToInt32(inv.AdditionalDocumentReference.Where(element => element.DocumentTypeCode.Value == "CUST_DES_ID").First().ID.Value), fat.Despatches[0].ReturnValue);
                            sb.AppendLine("e-İrsaliye başarıyla gönderildi. \nEvrak No: " + fat.Despatches[0].DespatchId);
                        }
                        return sb.ToString();
                    case "EDM":
                        var edmIrsaliye = new EDM.DespatchWebService();
                        edmIrsaliye.WebServisAdresDegistir();
                        edmIrsaliye.Login();
                        Connector.m.PkEtiketi = PK;
                        var edmResult = edmIrsaliye.TopluEIrsaliyeGonder(Irsaliyeler);

                        foreach (var doc in edmResult.DESPATCH)
                        {
                            var fat = edmIrsaliye.GonderilenEIrsaliyeIndir(doc.UUID);
                            XmlSerializer serializer = new XmlSerializer(typeof(DespatchAdviceType));
                            DespatchAdviceType inv = (DespatchAdviceType)serializer.Deserialize(new MemoryStream(fat[0].CONTENT.Value));

                            Result.DurumAciklama = fat[0].HEADER.STATUS_DESCRIPTION;
                            Result.DurumKod = "1";
                            Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                            Result.EvrakNo = fat[0].ID;
                            Result.UUID = fat[0].UUID;
                            Result.ZarfUUID = fat[0].HEADER.ENVELOPE_IDENTIFIER ?? "";

                            Entegrasyon.UpdateEirsaliye(Result, Convert.ToInt32(inv.AdditionalDocumentReference.Where(element => element.DocumentTypeCode.Value == "CUST_DES_ID").First().ID.Value), fat[0].CONTENT.Value);
                            sb.AppendLine("e-İrsaliye başarıyla gönderildi. \nEvrak No: " + fat[0].ID);
                        }
                        return sb.ToString();
                    case "QEF":
                        var qefIrsaliye = new QEF.DespatchAdviceService();

                        foreach (var Irsaliye in Irsaliyeler)
                        {
                            int EVRAKSN = Convert.ToInt32(Irsaliye.AdditionalDocumentReference.Where(element => element.DocumentTypeCode.Value == "CUST_DES_ID").First().ID.Value);
                            DespatchAdviceSerializer qefSerializer = new DespatchAdviceSerializer();
                            var strFatura = qefSerializer.GetXmlAsString(Irsaliye);

                            var qefResult = qefIrsaliye.IrsaliyeGonder(strFatura, EVRAKSN, Irsaliye.IssueDate.Value.Date); ;

                            var qefFaturaUBL = qefIrsaliye.IrsaliyeUBLIndir(new[] { Irsaliye.UUID.Value });

                            Result.DurumAciklama = qefResult.aciklama ?? "";
                            Result.DurumKod = qefResult.gonderimDurumu.ToString();
                            Result.DurumZaman = DateTime.TryParse(qefResult.gonderimTarihi, out DateTime dz) ? dz : new DateTime(1900, 1, 1);
                            Result.EvrakNo = qefResult.belgeNo;
                            Result.UUID = qefResult.ettn;
                            Result.ZarfUUID = "";

                            Entegrasyon.UpdateEirsaliye(Result, EVRAKSN, qefFaturaUBL.First().Value);
                            sb.AppendLine("e-İrsaliye başarıyla gönderildi. \nEvrak No: " + Result.EvrakNo);
                        }
                        return sb.ToString();
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
            public static void Indir(DateTime day1, DateTime day2)
            {
                var Result = new Results.EFAGLN();
                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var fitIrsaliye = new FIT.DespatchWebService();
                        fitIrsaliye.WebServisAdresDegistir();

                        var list = new List<string>();
                        List<GetDesUBLListResponseType> fitGelen = new List<GetDesUBLListResponseType>();
                        for (; day1.Date <= day2.Date; day1 = day1.AddDays(1))
                        {
                            Connector.m.IssueDate = new DateTime(day1.Year, day1.Month, day1.Day, 0, 0, 0);
                            Connector.m.EndDate = new DateTime(day1.Year, day1.Month, day1.Day, 23, 59, 59);

                            var gond = fitIrsaliye.GelenIrsaliyeler();
                            if (gond.Response != null)
                            {
                                if (gond.Response.Length > 0)
                                {
                                    foreach (var fat in gond.Response)
                                    {
                                        list.Add(fat.UUID);
                                        fitGelen.Add(fat);
                                    }
                                }
                            }
                        }

                        var lists = list.Split(20);

                        foreach (var l in lists)
                        {
                            var ubls = fitIrsaliye.GelenIrsaliyeUBLIndir(l.ToArray());
                            foreach (var ubl in ubls.Response)
                            {
                                GetDesUBLListResponseType gln = null;
                                foreach (var g in fitGelen)
                                {
                                    if (g.UUID == ubl.UUID)
                                        gln = g;
                                }

                                Result.DurumAciklama = "";
                                Result.DurumKod = "";
                                Result.DurumZaman = gln.InsertDateTime;
                                Result.Etiket = gln.Identifier;
                                Result.EvrakNo = gln.ID;
                                Result.UUID = gln.UUID;
                                Result.VergiHesapNo = gln.VKN_TCKN;
                                Result.ZarfUUID = gln.EnvUUID.ToString();

                                Entegrasyon.InsertIrsaliye(Result, ZipUtility.UncompressFile(ubl.DocData));
                            }
                        }
                        break;
                    case "DPLANET":
                        var dpIrsaliye = new DigitalPlanet.DespatchWebService();
                        dpIrsaliye.Login();
                        Connector.m.IssueDate = new DateTime(day1.Year, day1.Month, day1.Day, 0, 0, 0);
                        Connector.m.EndDate = new DateTime(day2.Year, day2.Month, day2.Day, 23, 59, 59);

                        var dpGelen = dpIrsaliye.GelenEIrsaliyeler();
                        if (dpGelen.Despatches.Count() > 0)
                        {
                            foreach (var ubl in dpGelen.Despatches)
                            {
                                Connector.m.GbEtiketi = ubl.SenderPostBoxName;
                                var doc = dpIrsaliye.GelenEIrsaliyeIndir(ubl.UUID);

                                Result.DurumAciklama = ubl.StatusDescription;
                                Result.DurumKod = ubl.StatusCode + "";
                                Result.DurumZaman = ubl.Issuetime;
                                Result.Etiket = ubl.SenderPostBoxName;
                                Result.EvrakNo = ubl.DespatchId;
                                Result.UUID = ubl.UUID;
                                Result.VergiHesapNo = ubl.Sendertaxid;
                                Result.ZarfUUID = "";

                                Entegrasyon.InsertIrsaliye(Result, doc.Despatches[0].ReturnValue);
                            }
                        }
                        break;
                    case "EDM":
                        var edmIrsaliye = new EDM.DespatchWebService();
                        edmIrsaliye.Login();
                        Connector.m.IssueDate = new DateTime(day1.Year, day1.Month, day1.Day, 0, 0, 0);
                        Connector.m.EndDate = new DateTime(day2.Year, day2.Month, day2.Day, 23, 59, 59);

                        var edmGelen = edmIrsaliye.GenelEIrsaliyeler();
                        if (edmGelen.Count() > 0)
                        {
                            foreach (var ubl in edmGelen)
                            {
                                Connector.m.GbEtiketi = ubl.HEADER.FROM;
                                var doc = edmIrsaliye.EIrsaliyeIndir(ubl.UUID);

                                Result.DurumAciklama = doc[0].HEADER.GIB_STATUS_CODESpecified ? doc[0].HEADER.GIB_STATUS_DESCRIPTION : "0";
                                Result.DurumKod = doc[0].HEADER.GIB_STATUS_CODESpecified ? doc[0].HEADER.GIB_STATUS_CODE + "" : "0";
                                Result.DurumZaman = doc[0].HEADER.ISSUE_DATE;
                                Result.Etiket = doc[0].HEADER.FROM;
                                Result.EvrakNo = doc[0].ID;
                                Result.UUID = doc[0].UUID;
                                Result.VergiHesapNo = doc[0].HEADER.SENDER;
                                Result.ZarfUUID = doc[0].HEADER.ENVELOPE_IDENTIFIER ?? "";

                                Entegrasyon.InsertIrsaliye(Result, doc[0].CONTENT.Value);
                            }
                        }
                        break;
                    case "QEF":
                        var qefFatura = new QEF.GetDespatchAdviceService();
                        foreach (var fatura in qefFatura.GelenEIrsaliyeler(day1, day2))
                        {
                            Result.DurumAciklama = fatura.Value.yanitGonderimCevabiDetayi ?? "";
                            Result.DurumKod = fatura.Value.yanitGonderimCevabiKodu + "";
                            Result.DurumZaman = DateTime.ParseExact(fatura.Key.belgeTarihi, "yyyyMMdd", CultureInfo.CurrentCulture);
                            Result.Etiket = fatura.Key.gonderenEtiket;
                            Result.EvrakNo = fatura.Value.belgeNo;
                            Result.UUID = fatura.Value.ettn;
                            Result.VergiHesapNo = fatura.Key.gonderenVknTckn;
                            Result.ZarfUUID = "";

                            var ubl = ZipUtility.UncompressFile(qefFatura.GelenUBLIndir(fatura.Value.ettn));
                            Entegrasyon.InsertIrsaliye(Result, ubl);
                        }
                        break;
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
            public static void Kabul(string GUID, string Aciklama)
            {
                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var dosya = Entegrasyon.GelenDosya(GUID);

                        XmlSerializer serializer = new XmlSerializer(typeof(UblDespatchAdvice.DespatchAdviceType));
                        var desp = (UblDespatchAdvice.DespatchAdviceType)serializer.Deserialize(new MemoryStream(dosya));

                        var yanit = new IrsaliyeYanitiUBL();
                        yanit.CreateReceiptAdvice(desp, Aciklama);

                        foreach (var iy in desp.DespatchLine)
                            yanit.AddReceiptLine(iy, iy.DeliveredQuantity.Value, 0, 0, 0);

                        yanit.GetYanit().ID = new UblReceiptAdvice.IDType { Value = Entegrasyon.GetIrsaliyeYanitEvrakNo() };

                        var fitIrsaliye = new FIT.DespatchWebService();
                        fitIrsaliye.WebServisAdresDegistir();

                        var res = fitIrsaliye.IrsaliyeYanitiGonder(yanit.GetYanit());
                        var fitDosya = fitIrsaliye.GonderilenIrsaliyeYanitlari(res.Response[0].EnvUUID, res.Response[0].UUID);
                        var fitYnt = Entegrasyon.ConvertToYanit(fitDosya, "GDN", res.Response[0].EnvUUID);
                        Entegrasyon.InsertIntoEirYnt(fitYnt);

                        Entegrasyon.UpdateEfaglnStatus(new Results.EFAGLN { ZarfUUID = res.Response[0].EnvUUID }, GUID, true, Aciklama);
                        break;
                    case "DPLANET":
                        var dpIrsaliye = new DigitalPlanet.DespatchWebService();
                        dpIrsaliye.WebServisAdresDegistir();

                        var result = dpIrsaliye.EIrsaliyeCevap(GUID, true, Aciklama);

                        if (result.ServiceResult == COMMON.dpDespatch.Result.Error)
                            throw new Exception(result.ServiceResultDescription);

                        var irsaliyeYanit = dpIrsaliye.GidenEIrsaliyeYanitIndir(result.Receipments[0].UUID);
                        var ynt = Entegrasyon.ConvertToYanit(irsaliyeYanit.Receipments[0], "GDN");

                        Entegrasyon.InsertIntoEirYnt(ynt);

                        Entegrasyon.UpdateEfaglnStatus(new Results.EFAGLN { ZarfUUID = result.Receipments[0].ReceiptmentId }, GUID, true, Aciklama);
                        break;
                    case "EDM":
                        throw new Exception("Entegratör Bu Eylemi Desteklememektedir!");
                    case "QEF":
                        throw new Exception("Entegratör Bu Eylemi Desteklememektedir!");
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
            public static void Red(string GUID, string Aciklama)
            {
                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var dosya = Entegrasyon.GelenDosya(GUID);

                        XmlSerializer serializer = new XmlSerializer(typeof(UblDespatchAdvice.DespatchAdviceType));
                        var desp = (UblDespatchAdvice.DespatchAdviceType)serializer.Deserialize(new MemoryStream(dosya));

                        var yanit = new IrsaliyeYanitiUBL();
                        yanit.CreateReceiptAdvice(desp, Aciklama);

                        foreach (var iy in desp.DespatchLine)
                            yanit.AddReceiptLine(iy, 0, iy.DeliveredQuantity.Value, 0, 0);

                        yanit.GetYanit().ID = new UblReceiptAdvice.IDType { Value = Entegrasyon.GetIrsaliyeYanitEvrakNo() };

                        var fitIrsaliye = new FIT.DespatchWebService();
                        fitIrsaliye.WebServisAdresDegistir();

                        var res = fitIrsaliye.IrsaliyeYanitiGonder(yanit.GetYanit());
                        var fitDosya = fitIrsaliye.GonderilenIrsaliyeYanitlari(res.Response[0].EnvUUID, res.Response[0].UUID);
                        var fitYnt = Entegrasyon.ConvertToYanit(fitDosya, "GDN", res.Response[0].EnvUUID);
                        Entegrasyon.InsertIntoEirYnt(fitYnt);

                        Entegrasyon.UpdateEfaglnStatus(new Results.EFAGLN { ZarfUUID = res.Response[0].EnvUUID }, GUID, true, Aciklama);
                        break;
                    case "DPLANET":
                        var dpIrsaliye = new DigitalPlanet.DespatchWebService();
                        dpIrsaliye.WebServisAdresDegistir();

                        var result = dpIrsaliye.EIrsaliyeCevap(GUID, false, Aciklama);

                        if (result.ServiceResult == COMMON.dpDespatch.Result.Error)
                            throw new Exception(result.ServiceResultDescription);


                        var irsaliyeYanit = dpIrsaliye.GidenEIrsaliyeYanitIndir(result.Receipments[0].UUID);
                        var ynt = Entegrasyon.ConvertToYanit(irsaliyeYanit.Receipments[0], "GDN");

                        Entegrasyon.InsertIntoEirYnt(ynt);

                        Entegrasyon.UpdateEfaglnStatus(new Results.EFAGLN { ZarfUUID = result.Receipments[0].ReceiptmentId }, GUID, false, Aciklama);
                        break;
                    case "EDM":
                        throw new Exception("Entegratör Bu Eylemi Desteklememektedir!");
                    case "QEF":
                        throw new Exception("Entegratör Bu Eylemi Desteklememektedir!");
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
            public static void GonderilenGuncelle(DateTime start, DateTime end)
            {
                var Result = new Results.EFAGDN();
                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var fitIrsaliye = new FIT.DespatchWebService();
                        fitIrsaliye.WebServisAdresDegistir();

                        for (; start.Date <= end.Date; start = start.AddDays(1))
                        {
                            var evraklar = Entegrasyon.GidenIrsaliyeGUIDList(start, Connector.m.GbEtiketi);

                            foreach (var evrak in evraklar.Where(elm => !string.IsNullOrEmpty(elm.Item1)))
                            {
                                try
                                {
                                    var fitGonderilenler = fitIrsaliye.IrsaliyeYanitiIndir(evrak.Item1);

                                    foreach (var yanit in fitGonderilenler.Response)
                                    {
                                        if (yanit.Receipts == null)
                                            continue;

                                        var rcp = Entegrasyon.ConvertToYanitList(yanit, "GLN", evrak.Item2);
                                        foreach (var r in rcp)
                                            Entegrasyon.InsertIntoEirYnt(r);
                                    }
                                }
                                catch (Exception) { }
                            }
                            //var yanitlar = fitIrsaliye.GelenEIrsaliyeYanit(start, end);
                            //if (yanitlar.ServiceResult == COMMON.dpDespatch.Result.Error)
                            //    throw new Exception(yanitlar.ServiceResultDescription);

                            //foreach (var Receipment in yanitlar.Receipments.Where(elm => elm.Direction == COMMON.dpDespatch.Direction.Incoming))
                            //{
                            //    var rcp = Entegrasyon.ConvertToYanit(dpIrsaliye.GelenEIrsaliyeYanit(Receipment.UUID).Receipments[0], "GLN");
                            //    rcp.REFEVRAKGUID = Receipment.DespatchUUID;
                            //    rcp.REFEVRAKNO = Receipment.DespatchId;
                            //    Entegrasyon.InsertIntoEirYnt(rcp);
                            //}
                        }
                        break;
                    case "DPLANET":
                        var dpIrsaliye = new DigitalPlanet.DespatchWebService();
                        dpIrsaliye.WebServisAdresDegistir();
                        dpIrsaliye.Login();

                        /*
						var yanit = dpIrsaliye.EIrsaliyeDurumSorgula(start, end);
						//var gonderilenler = dpIrsaliye.GonderilenEIrsaliyeler(start, end);
						if (yanit.ServiceResult == COMMON.dpDespatch.Result.Error)
							throw new Exception(yanit.ServiceResultDescription);

						foreach (var ynt in yanit.Despatches)
						{
							Result.DurumAciklama = ynt.StatusDescription;
							Result.DurumKod = ynt.StatusCode + "";
							Result.DurumZaman = ynt.Issuetime;
							Result.EvrakNo = ynt.DespatchId;
							Result.UUID = ynt.UUID;
							Result.ZarfUUID = "";

							var fat = dpIrsaliye.GonderilenEIrsaliyeIndir(ynt.UUID);
							var fatBytes = fat.Despatches[0].ReturnValue;

							///Entegrasyon.UpdateEfagdnStatus(Result);
							Entegrasyon.UpdateEIrsaliye(Result, fatBytes);
						}
						*/

                        var yanitlar = dpIrsaliye.GelenEIrsaliyeYanit(start, end);
                        if (yanitlar.ServiceResult == COMMON.dpDespatch.Result.Error)
                            throw new Exception(yanitlar.ServiceResultDescription);

                        foreach (var Receipment in yanitlar.Receipments.Where(elm => elm.Direction == COMMON.dpDespatch.Direction.Incoming))
                        {
                            var rcp = Entegrasyon.ConvertToYanit(dpIrsaliye.GelenEIrsaliyeYanit(Receipment.UUID).Receipments[0], "GLN");
                            rcp.REFEVRAKGUID = Receipment.DespatchUUID;
                            rcp.REFEVRAKNO = Receipment.DespatchId;
                            Entegrasyon.InsertIntoEirYnt(rcp);
                        }
                        break;
                    case "EDM":
                        var edmIrsaliye = new EDM.DespatchWebService();
                        edmIrsaliye.WebServisAdresDegistir();
                        edmIrsaliye.Login();

                        var edmGonderilenler = edmIrsaliye.GonderilenEIrsaliyeler(start, end);

                        foreach (var ynt in edmGonderilenler)
                        {
                            Result.DurumAciklama = ynt.HEADER.GIB_STATUS_CODESpecified ? ynt.HEADER.GIB_STATUS_DESCRIPTION : "0";
                            Result.DurumKod = ynt.HEADER.GIB_STATUS_CODESpecified ? ynt.HEADER.GIB_STATUS_CODE + "" : "0";
                            Result.DurumZaman = ynt.HEADER.ISSUE_DATE;
                            Result.EvrakNo = ynt.ID;
                            Result.UUID = ynt.UUID;
                            Result.ZarfUUID = "";

                            Entegrasyon.UpdateEfagdnStatus(Result);
                        }
                        break;
                    case "QEF":
                        var qefIrsaliye = new QEF.DespatchAdviceService();
                        var qefGelen = qefIrsaliye.GonderilenIrsaliyeler(start, end);

                        //var edmYanitlar = edmFatura.GelenUygulamaYanitlari(day1, day2);

                        foreach (var fatura in qefGelen)
                        {
                            if (fatura.alimDurumu == "İşlendi" && fatura.ettn != null)
                                Entegrasyon.UpdateEfagdnGonderimDurum(fatura.ettn, 3);
                            else if (fatura.alimDurumu == "İşleme Hatası" && fatura.ettn != null)
                                Entegrasyon.UpdateEfagdnGonderimDurum(fatura.ettn, 0);

                            if (fatura.ettn != null)
                            {
                                Result.DurumAciklama = fatura.hataMesaji ?? "";
                                Result.DurumKod = "";
                                Result.DurumZaman = DateTime.TryParseExact(fatura.alimZamani, "yyyyMMdd", CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime dt) ? dt : new DateTime(1900, 1, 1);
                                Result.UUID = fatura.ettn;
                                Result.ZarfUUID = "";

                                Entegrasyon.UpdateEfagdnStatus(Result);
                            }
                        }
                        break;
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
            public static void GonderilenGuncelle(List<string> UUIDs)
            {
                var Result = new Results.EFAGDN();
                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var fitIrsaliye = new FIT.DespatchWebService();
                        fitIrsaliye.WebServisAdresDegistir();

                        var fitIRsaliyeler = fitIrsaliye.GidenIrsaliyeUBLIndir(UUIDs.ToArray());

                        foreach (var f in fitIRsaliyeler.Response)
                            Entegrasyon.UpdateEfadosIrsaliye(ZipUtility.UncompressFile(f.DocData));

                        break;
                    case "DPLANET":
                        var dpIrsaliye = new DigitalPlanet.DespatchWebService();
                        dpIrsaliye.WebServisAdresDegistir();
                        dpIrsaliye.Login();

                        List<byte[]> dosyalar = new List<byte[]>();
                        foreach (string UUID in UUIDs)
                        {
                            var gond = dpIrsaliye.GonderilenEIrsaliyeIndir(UUID);
                            dosyalar.Add(gond.Despatches[0].ReturnValue);
                        }

                        foreach (var dosya in dosyalar)
                            Entegrasyon.UpdateEfadosIrsaliye(dosya);
                        break;
                    case "EDM":
                        var edmIrsaliye = new EDM.DespatchWebService();
                        edmIrsaliye.WebServisAdresDegistir();
                        edmIrsaliye.Login();


                        List<byte[]> edmDosyalar = new List<byte[]>();
                        foreach (var UUID in UUIDs)
                            edmDosyalar.Add(edmIrsaliye.EIrsaliyeIndir(UUID)[0].CONTENT.Value);

                        foreach (var dosya in edmDosyalar)
                            Entegrasyon.UpdateEfadosIrsaliye(dosya);
                        break;
                    case "QEF":
                        var qefIrsaliye = new QEF.DespatchAdviceService();

                        var qefDosyalar = qefIrsaliye.IrsaliyeUBLIndir(UUIDs.ToArray());

                        foreach (var dosya in qefDosyalar)
                            Entegrasyon.UpdateEfadosIrsaliye(dosya.Value);
                        break;
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
            public static void Esle(string GUID)
            {
                var Result = new Results.EFAGDN();
                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var fitIrsaliye = new FIT.DespatchWebService();
                        fitIrsaliye.WebServisAdresDegistir();

                        var fitIrsaliyeler = fitIrsaliye.GonderilenIrsaliyIndir(new[] { GUID });

                        foreach (var irs in fitIrsaliyeler.Response)
                        {
                            var fitZarf = fitIrsaliye.GonderilenZarflarIndir(new[] { irs.EnvUUID + "" });
                            Result.DurumAciklama = fitZarf.Response[0].Description;
                            Result.DurumKod = fitZarf.Response[0].ResponseCode;
                            Result.DurumZaman = fitZarf.Response[0].IssueDate;
                            Result.EvrakNo = irs.ID;
                            Result.UUID = irs.UUID;
                            Result.ZarfUUID = irs.EnvUUID + "";

                            ///Entegrasyon.UpdateEfagdnStatus(Result);
                            Entegrasyon.UpdateEIrsaliye(Result);
                        }

                        var fitYanitlar = fitIrsaliye.IrsaliyeYanitiIndir(GUID);
                        if (fitYanitlar.Response.Length > 0)
                        {
                            if (fitYanitlar.Response[0].Receipts != null)
                            {
                                foreach (var fitYanit in fitYanitlar.Response[0].Receipts)
                                {
                                    var recData = fitYanit.DocData;

                                    XmlSerializer ser = new XmlSerializer(typeof(ReceiptAdviceType));
                                    ReceiptAdviceType receipt = (ReceiptAdviceType)ser.Deserialize(new MemoryStream(ZipUtility.UncompressFile(recData)));

                                    var rejected = receipt.ReceiptLine.Any(elm => elm.RejectedQuantity?.Value != null);

                                    if (rejected)
                                    {
                                        Result = new Results.EFAGDN
                                        {
                                            DurumAciklama = receipt.ReceiptLine.FirstOrDefault(elm => elm.RejectedQuantity?.Value != null).RejectReason?[0]?.Value ?? "",
                                            DurumKod = "3",
                                            DurumZaman = receipt.IssueDate.Value,
                                            UUID = fitYanitlar.Response[0].DespatchUUID,
                                        };

                                        Entegrasyon.UpdateEfagdnStatus(Result);
                                    }
                                }
                            }
                        }
                        break;
                    case "DPLANET":
                        var dpIrsaliye = new DigitalPlanet.DespatchWebService();
                        dpIrsaliye.WebServisAdresDegistir();
                        dpIrsaliye.Login();

                        COMMON.dpDespatch.DespatchPackResult yanit = null;
                        string errorResult = "";

                        try
                        {
                            yanit = dpIrsaliye.GonderilenEIrsaliyeIndir(GUID.ToLower());
                            if (yanit.ServiceResult == COMMON.dpDespatch.Result.Error)
                            {
                                errorResult = yanit.ServiceResultDescription;
                                yanit = null;
                            }
                        }
                        catch (Exception ex)
                        {
                            if (appConfig.Debugging)
                                appConfig.DebuggingException(ex);

                            errorResult = ex.Message;
                            yanit = null;
                        }

                        try
                        {
                            if (yanit == null)
                            {
                                yanit = dpIrsaliye.GonderilenEIrsaliyeIndir(GUID.ToUpper());
                                if (yanit.ServiceResult == COMMON.dpDespatch.Result.Error)
                                {
                                    errorResult = yanit.ServiceResultDescription;
                                    yanit = null;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            if (appConfig.Debugging)
                                appConfig.DebuggingException(ex);

                            errorResult = ex.Message;
                            yanit = null;
                        }

                        if (yanit != null)
                        {
                            foreach (var ynt in yanit.Despatches)
                            {
                                Result.DurumAciklama = ynt.StatusDescription;
                                Result.DurumKod = ynt.StatusCode + "";
                                Result.DurumZaman = ynt.Issuetime;
                                Result.EvrakNo = ynt.DespatchId;
                                Result.UUID = ynt.UUID;
                                Result.ZarfUUID = "";

                                //var fat = dpIrsaliye.GonderilenEIrsaliyeIndir(ynt.UUID);
                                //var fatBytes = fat.Despatches[0].ReturnValue;

                                Entegrasyon.UpdateEIrsaliye(Result);

                                var yanitlar = dpIrsaliye.GelenEIrsaliyeYanitByIsaliyeNo(ynt.UUID);
                                if (yanitlar.ServiceResult == COMMON.dpDespatch.Result.Error)
                                {
                                    errorResult = yanit.ServiceResultDescription;
                                    yanit = null;
                                }

                                foreach (var Receipment in yanitlar.Receipments)
                                {
                                    var rcp = Entegrasyon.ConvertToYanit(Receipment, "GLN");
                                    Entegrasyon.InsertIntoEirYnt(rcp);
                                }
                            }
                        }
                        else if (errorResult != "" && yanit == null)
                            throw new Exception(errorResult);
                        break;
                    case "EDM":
                        var edmIrsaliye = new EDM.DespatchWebService();
                        edmIrsaliye.WebServisAdresDegistir();
                        edmIrsaliye.Login();

                        var edmGonderilenler = edmIrsaliye.EIrsaliyeIndir(GUID);

                        foreach (var ynt in edmGonderilenler)
                        {
                            Result.DurumAciklama = ynt.HEADER.GIB_STATUS_CODESpecified ? ynt.HEADER.GIB_STATUS_DESCRIPTION : "0";
                            Result.DurumKod = ynt.HEADER.GIB_STATUS_CODESpecified ? ynt.HEADER.GIB_STATUS_CODE + "" : "0";
                            Result.DurumZaman = ynt.HEADER.ISSUE_DATE;
                            Result.EvrakNo = ynt.ID;
                            Result.UUID = ynt.UUID;
                            Result.ZarfUUID = "";

                            Entegrasyon.UpdateEfagdnStatus(Result);
                        }
                        break;
                    case "QEF":
                        var qefIrsaliye = new QEF.DespatchAdviceService();

                        var qefGonderilenler = qefIrsaliye.GonderilenEIrsaliyeIndir(new[] { GUID });

                        foreach (var doc in qefGonderilenler)
                        {

                            Result.DurumAciklama = doc.Key.gonderimCevabiDetayi ?? "";
                            Result.DurumKod = doc.Key.gonderimCevabiKodu + "";
                            Result.DurumZaman = DateTime.TryParseExact(doc.Key.alimTarihi, "", CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime dt) ? dt : new DateTime(1900, 1, 1);
                            Result.EvrakNo = doc.Key.belgeNo;
                            Result.UUID = doc.Key.ettn;
                            Result.ZarfUUID = "";

                            Entegrasyon.UpdateEfagdnStatus(Result);
                        }
                        break;
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
            public static void Esle(int EVRAKSN, DateTime GonderimTarih, string EVRAKGUID)
            {
                var Result = new Results.EFAGDN();

                var start = new DateTime(GonderimTarih.Year, GonderimTarih.Month, GonderimTarih.Day, 0, 0, 0);
                var end = new DateTime(GonderimTarih.Year, GonderimTarih.Month, GonderimTarih.Day, 23, 59, 59);
                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var fitIrsaliye = new FIT.DespatchWebService();
                        fitIrsaliye.WebServisAdresDegistir();

                        var fitIrsaliyeler = fitIrsaliye.GonderilenIrsaliyIndir(new[] { EVRAKGUID });

                        foreach (var irs in fitIrsaliyeler.Response)
                        {
                            var fitZarf = fitIrsaliye.GonderilenZarflarIndir(new[] { irs.EnvUUID + "" });
                            Result.DurumAciklama = fitZarf.Response[0].Description;
                            Result.DurumKod = fitZarf.Response[0].ResponseCode;
                            Result.DurumZaman = fitZarf.Response[0].IssueDate;
                            Result.EvrakNo = irs.ID;
                            Result.UUID = irs.UUID;
                            Result.ZarfUUID = irs.EnvUUID + "";

                            ///Entegrasyon.UpdateEfagdnStatus(Result);
                            Entegrasyon.UpdateEIrsaliye(Result);
                        }

                        var fitYanitlar = fitIrsaliye.IrsaliyeYanitiIndir(EVRAKGUID);
                        if (fitYanitlar.Response.Length > 0)
                        {
                            if (fitYanitlar.Response[0].Receipts != null)
                            {
                                foreach (var fitYanit in fitYanitlar.Response[0].Receipts)
                                {
                                    var recData = fitYanit.DocData;

                                    XmlSerializer ser = new XmlSerializer(typeof(ReceiptAdviceType));
                                    ReceiptAdviceType receipt = (ReceiptAdviceType)ser.Deserialize(new MemoryStream(ZipUtility.UncompressFile(recData)));

                                    var rejected = receipt.ReceiptLine.Any(elm => elm.RejectedQuantity?.Value != null);

                                    if (rejected)
                                    {
                                        Result = new Results.EFAGDN
                                        {
                                            DurumAciklama = receipt.ReceiptLine.FirstOrDefault(elm => elm.RejectedQuantity?.Value != null).RejectReason?[0]?.Value ?? "",
                                            DurumKod = "3",
                                            DurumZaman = receipt.IssueDate.Value,
                                            UUID = fitYanitlar.Response[0].DespatchUUID,
                                        };

                                        Entegrasyon.UpdateEfagdnStatus(Result);
                                    }
                                }
                            }
                        }
                        break;
                    case "DPLANET":
                        var dpIrsaliye = new DigitalPlanet.DespatchWebService();
                        dpIrsaliye.WebServisAdresDegistir();
                        dpIrsaliye.Login();

                        var yanit = dpIrsaliye.GonderilenEIrsaliyeler(start, end);

                        if (yanit.ServiceResult == COMMON.dpDespatch.Result.Error)
                            throw new Exception(yanit.ServiceResultDescription);
                        else
                        {
                            foreach (var ynt in yanit.Despatches)
                            {
                                XmlSerializer serializer = new XmlSerializer(typeof(DespatchAdviceType));
                                var irsData = dpIrsaliye.GonderilenEIrsaliyeIndir(ynt.UUID);
                                var irs = (DespatchAdviceType)serializer.Deserialize(new MemoryStream(irsData.Despatches[0].ReturnValue));

                                var custInv = irs.AdditionalDocumentReference.Where(elm => elm.DocumentTypeCode.Value == "CUST_DES_ID");
                                if (custInv.Any())
                                {
                                    if (custInv.First().ID.Value == EVRAKSN + "")
                                    {
                                        Result.DurumAciklama = ynt.StatusDescription;
                                        Result.DurumKod = ynt.StatusCode + "";
                                        Result.DurumZaman = ynt.Issuetime;
                                        Result.EvrakNo = ynt.DespatchId;
                                        Result.UUID = ynt.UUID;
                                        Result.ZarfUUID = "";

                                        ///Entegrasyon.UpdateEfagdnStatus(Result);
                                        Entegrasyon.UpdateEIrsaliye(Result);
                                        break;
                                    }
                                }
                            }
                        }
                        break;
                    case "EDM":
                        var edmIrsaliye = new EDM.DespatchWebService();
                        edmIrsaliye.WebServisAdresDegistir();
                        edmIrsaliye.Login();

                        var edmGonderilenler = edmIrsaliye.GonderilenEIrsaliyeler(start, end);

                        foreach (var ynt in edmGonderilenler)
                        {
                            XmlSerializer serializer = new XmlSerializer(typeof(DespatchAdviceType));
                            var irs = (DespatchAdviceType)serializer.Deserialize(new MemoryStream(ynt.CONTENT.Value));

                            var custInv = irs.AdditionalDocumentReference.Where(elm => elm.DocumentTypeCode.Value == "CUST_DES_ID");
                            if (custInv.Any())
                            {
                                Result.DurumAciklama = ynt.HEADER.GIB_STATUS_CODESpecified ? ynt.HEADER.GIB_STATUS_DESCRIPTION : "0";
                                Result.DurumKod = ynt.HEADER.GIB_STATUS_CODESpecified ? ynt.HEADER.GIB_STATUS_CODE + "" : "0";
                                Result.DurumZaman = ynt.HEADER.ISSUE_DATE;
                                Result.EvrakNo = ynt.ID;
                                Result.UUID = ynt.UUID;
                                Result.ZarfUUID = "";

                                ///Entegrasyon.UpdateEfagdnStatus(Result);
                                Entegrasyon.UpdateEIrsaliye(Result);
                                break;
                            }
                        }
                        break;
                    case "QEF":
                        var qefIrsaliye = new QEF.DespatchAdviceService();

                        var qefGonderilenler = qefIrsaliye.GonderilenIrsaliyeler(start, end);

                        foreach (var doc in qefGonderilenler)
                        {
                            if (doc.yerelBelgeNo == EVRAKSN.ToString())
                            {
                                Result.DurumAciklama = doc.hataMesaji ?? "";
                                Result.DurumKod = "";
                                Result.DurumZaman = DateTime.TryParseExact(doc.alimZamani, "yyyyMMdd", CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime dt) ? dt : new DateTime(1900, 1, 1);
                                Result.UUID = doc.ettn;

                                Entegrasyon.UpdateEIrsaliye(Result);
                                break;
                            }
                        }
                        break;
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
        }
        public static class EMustahsil
        {
            public static string Gonder(int EVRAKSN)
            {
                MmUBL mm = new MmUBL();
                var createdUBL = mm.CreateCreditNote(EVRAKSN);  // e-Mm  UBL i oluşturulur
                Connector.m.PkEtiketi = mm.PK;
                if (Connector.m.SchematronKontrol)
                {
                    var schematronResult = SchematronChecker.Check(createdUBL, SchematronDocType.eArsiv);
                    if (schematronResult.SchemaResult != "Başarılı" || schematronResult.SchematronResult != "Başarılı")
                        throw new Exception(schematronResult.Detail);
                }
                UBLBaseSerializer serializer = new MmSerializer();  // UBL  XML e dönüştürülür
                var strFatura = serializer.GetXmlAsString(createdUBL); // XML byte tipinden string tipine dönüştürülür.


                var docs = new Dictionary<object, byte[]>();
                docs.Add(createdUBL.UUID.Value + ".xml", Encoding.UTF8.GetBytes(strFatura));

                var Result = new Results.EFAGDN();

                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var ingEMustahsil = new FIT.MmWebService();
                        var fitResult = ingEMustahsil.EMmGonder(docs, createdUBL.UUID.Value);
                        var fitDosya = ingEMustahsil.MmUBLIndir(fitResult[0].ID, fitResult[0].UUID);

                        Result.DurumAciklama = fitDosya[0].ResultDescription;
                        Result.DurumKod = fitDosya[0].Result + "";
                        Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                        Result.EvrakNo = fitDosya[0].ID;
                        Result.UUID = fitDosya[0].UUID;
                        Result.ZarfUUID = "";

                        Entegrasyon.UpdateMustahsil(Result, EVRAKSN, ZipUtility.UncompressFile(fitDosya[0].DocData));
                        return "e-Müstahsil Makbuzu başarıyla gönderildi. Makbuz ID:" + fitResult[0].ID;
                    case "DPLANET":
                        createdUBL.CreditNoteTypeCode = null;
                        createdUBL.DocumentCurrencyCode = null;
                        createdUBL.TaxCurrencyCode = null;
                        strFatura = serializer.GetXmlAsString(createdUBL); // XML byte tipinden string tipine dönüştürülür.
                        var dpEMustahsil = new DigitalPlanet.MustahsilWebService();
                        var dpResult = dpEMustahsil.MustahsilGonder(strFatura);

                        if (dpResult.ServiceResult == COMMON.dpMustahsil.Result.Error)
                            throw new Exception(dpResult.ServiceResultDescription);

                        Result.DurumAciklama = dpResult.Receipts[0].StatusDescription;
                        Result.DurumKod = dpResult.Receipts[0].StatusCode + "";
                        Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                        Result.EvrakNo = dpResult.Receipts[0].ReceiptId;
                        Result.UUID = dpResult.Receipts[0].UUID;
                        Result.ZarfUUID = "";

                        Entegrasyon.UpdateMustahsil(Result, EVRAKSN, dpResult.Receipts[0].ReturnValue);
                        return "e-Müstahsil Makbuzu başarıyla gönderildi. Makbuz ID:" + dpResult.Receipts[0].ReceiptId;
                    case "EDM":
                        var edmEMustahsil = new EDM.MustahsilWebService();
                        var edmResult = edmEMustahsil.MustahsilGonder(strFatura);

                        var edmMmUbl = edmEMustahsil.MustahsilIndir(edmResult.MM[0].UUID);

                        Result.DurumAciklama = edmMmUbl[0].HEADER.STATUS_DESCRIPTION;
                        Result.DurumKod = "";
                        Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                        Result.EvrakNo = edmMmUbl[0].ID;
                        Result.UUID = edmMmUbl[0].UUID;
                        Result.ZarfUUID = "";

                        Entegrasyon.UpdateMustahsil(Result, EVRAKSN, edmMmUbl[0].CONTENT.Value);
                        return "e-Müstahsil Makbuzu başarıyla gönderildi. Makbuz ID:" + Result.EvrakNo;
                    case "QEF":
                        var sube = mm.SUBE;
                        sube = sube == "default" ? "DFLT" : sube;

                        var qefEMustahsil = new QEF.MustahsilService();
                        var qefResult = qefEMustahsil.MustahsilGonder(strFatura, Connector.m.VknTckn, EVRAKSN, sube);

                        if (appConfig.Debugging)
                        {
                            if (!Directory.Exists("C:\\ReqResp\\QEF"))
                                Directory.CreateDirectory("C:\\ReqResp\\QEF");

                            File.WriteAllText("C:\\ReqResp\\QEF\\Resp_" + Guid.NewGuid() + ".json", JsonConvert.SerializeObject(qefResult));
                        }

                        var ByteData = qefResult.Belge.belgeIcerigi;

                        Result.DurumAciklama = qefResult.Result.resultText;
                        Result.DurumKod = qefResult.Result.resultCode;
                        Result.DurumZaman = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Second, 0);
                        Result.EvrakNo = qefResult.Result.resultExtra.First(elm => elm.key.ToString() == "belgeNo").value.ToString();
                        Result.UUID = qefResult.Result.resultExtra.First(elm => elm.key.ToString() == "uuid").value.ToString();
                        Result.ZarfUUID = "";
                        Result.YanitDurum = 0;

                        Entegrasyon.UpdateEfagdn(Result, EVRAKSN, ByteData, t: typeof(UBL.UBLObject.MmObject.CreditNoteType));

                        return "e-Müstahsil Makbuzu başarıyla gönderildi. Makbuz ID:" + Result.EvrakNo;
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
            public static string Iptal(int EVRAKSN, string UUID, string EVRAKNO, decimal TOTAL)
            {
                switch (UrlModel.SelectedItem)
                {
                    case "FIT":
                    case "ING":
                    case "INGBANK":
                        var ingEMustahsil = new FIT.MmWebService();
                        var fitResult = ingEMustahsil.EMmIptal(EVRAKNO, TOTAL);
                        Entegrasyon.SilEarsiv(EVRAKSN, TOTAL, fitResult[0].ResultDescription, DateTime.Now);
                        return fitResult[0].ResultDescription;
                    case "DPLANET":
                        var dpEMustahsil = new DigitalPlanet.MustahsilWebService();
                        var dpResult = dpEMustahsil.MustahsilIptal(UUID, TOTAL);
                        if (dpResult.ServiceResult == COMMON.dpMustahsil.Result.Error)
                            throw new Exception(dpResult.ServiceResultDescription);
                        Entegrasyon.SilEarsiv(EVRAKSN, TOTAL, dpResult.StatusDescription, DateTime.Now);
                        return "e-Müstahsil Makbuzu başarıyla iptal edildi. Makbuz ID:" + dpResult.ReceiptId;
                    case "EDM":
                        var edmEMustahsil = new EDM.MustahsilWebService();
                        var edmResult = edmEMustahsil.MustahsilIptal(UUID, TOTAL);

                        Entegrasyon.SilEarsiv(EVRAKSN, TOTAL, "", DateTime.Now);
                        return "e-Müstahsil Makbuzu başarıyla iptal edildi.";
                    case "QEF":
                        var qefEMustahsil = new QEF.MustahsilService();
                        var qefpResult = qefEMustahsil.IptalFatura(UUID);

                        Entegrasyon.SilEarsiv(EVRAKSN, TOTAL, qefpResult.resultText, DateTime.Now);

                        return "e-Müstahsil Makbuzu başarıyla iptal edildi.";
                    default:
                        throw new Exception("Tanımlı Entegratör Bulunamadı!");
                }
            }
        }
        public class EArsivYanit
        {
            public string Mesaj { get; set; }
            public bool KagitNusha { get; set; }
            public byte[] Dosya { get; set; }
        }
        public class AlinanBelge
        {
            public string GBUNVAN { get; set; }
            public string GBETIKET { get; set; }
            public string EVRAKNO { get; set; }
            public DateTime YUKLEMEZAMAN { get; set; }
            public Guid EVRAKGUID { get; set; }
            public bool EKSIK { get; set; }
        }
    }
}