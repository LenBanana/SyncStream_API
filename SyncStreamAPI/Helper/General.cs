﻿using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;

namespace SyncStreamAPI.Helper
{
    public class General
    {
        private static Random random = new Random();

        private static List<string> GallowWords = new List<string>()
        {
            "Ärger",
            "Ärztin",
            "Abend",
            "Abfahrt",
            "Abflug",
            "Absender",
            "Adresse",
            "Alkohol",
            "Alter",
            "Ampel",
            "Anfang",
            "Angebot",
            "Angestellte",
            "Angst",
            "Ankunft",
            "Anmeldung",
            "Anrede",
            "Anruf",
            "Anrufbeantworter",
            "Ansage",
            "Anschluss",
            "Antwort",
            "Anzeige",
            "Anzug",
            "Apfel",
            "Apotheke",
            "Appartement",
            "Appetit",
            "April",
            "Arbeit",
            "Arbeitsplatz",
            "Arm",
            "Arzt",
            "Aufenthalt",
            "Aufgabe",
            "Aufzug",
            "Auge",
            "August",
            "Ausbildung",
            "Ausflug",
            "Ausgang",
            "Auskunft",
            "Ausländer",
            "Ausländerin",
            "Ausland",
            "Aussage",
            "Ausstellung",
            "Ausweis",
            "Auto",
            "Autobahn",
            "Automat",
            "Bäckerei",
            "Büro",
            "Baby",
            "Bad",
            "Bahn",
            "Bahnhof",
            "Bahnsteig",
            "Balkon",
            "Banane",
            "Bank",
            "Batterie",
            "Baum",
            "Beamte",
            "Beamtin",
            "Bein",
            "Beispiel",
            "Bekannte",
            "Benzin",
            "Beratung",
            "Berg",
            "Beruf",
            "Berufsschule",
            "Besuch",
            "Betrag",
            "Bett",
            "Bewerbung",
            "Bier",
            "Bild",
            "Bildschirm",
            "Birne",
            "Bitte",
            "Blatt",
            "Bleistift",
            "Blick",
            "Blume",
            "Bluse",
            "Blut",
            "Bogen",
            "Bohne",
            "Brötchen",
            "Brücke",
            "Brief",
            "Briefkasten",
            "Briefmarke",
            "Brieftasche",
            "Briefumschlag",
            "Brille",
            "Brot",
            "Bruder",
            "Buch",
            "Buchstabe",
            "Bus",
            "Butter",
            "Café",
            "CD",
            "CD-ROM",
            "Chef",
            "Computer",
            "Creme",
            "Dach",
            "Dame",
            "Dank",
            "Datum",
            "Dauer",
            "Deutsche",
            "Dezember",
            "Dienstag",
            "Ding",
            "Disco",
            "Doktor",
            "Dom",
            "Donnerstag",
            "Doppelzimmer",
            "Dorf",
            "Drucker",
            "Durchsage",
            "Durst",
            "Dusche",
            "E-Mail",
            "Ecke",
            "Ehefrau",
            "Ehemann",
            "Ei",
            "Einführung",
            "Eingang",
            "Einladung",
            "Eintritt",
            "Einwohner",
            "Einzelzimmer",
            "Eis",
            "Eltern",
            "Empfänger",
            "Empfang",
            "Ende",
            "Enkel",
            "Entschuldigung",
            "Erdgeschoss",
            "Erfahrung",
            "Ergebnis",
            "Erlaubnis",
            "Ermäßigung",
            "Erwachsene",
            "Essen",
            "Export",
            "Fähre",
            "Führerschein",
            "Führung",
            "Fabrik",
            "Fahrer",
            "Fahrkarte",
            "Fahrplan",
            "Fahrrad",
            "Familie",
            "Familienname",
            "Familienstand",
            "Farbe",
            "Fax",
            "Februar",
            "Fehler",
            "Fenster",
            "Ferien",
            "Fernsehgerät",
            "Fest",
            "Feuer",
            "Feuerwehr",
            "Feuerzeug",
            "Fieber",
            "Film",
            "Firma",
            "Fisch",
            "Flasche",
            "Fleisch",
            "Flughafen",
            "Flugzeug",
            "Flur",
            "Fluss",
            "Formular",
            "Foto",
            "Fotoapparat",
            "Frühjahr",
            "Frühling",
            "Frühstück",
            "Frage",
            "Frau",
            "Freitag",
            "Freizeit",
            "Freund",
            "Freundin",
            "Friseur",
            "Frist",
            "Fuß",
            "Fußball",
            "Fundbüro",
            "Gabel",
            "Garage",
            "Garten",
            "Gas",
            "Gast",
            "Gebühr",
            "Geburtsjahr",
            "Geburtsort",
            "Geburtstag",
            "Gegenteil",
            "Geld",
            "Geldbörse",
            "Gemüse",
            "Gepäck",
            "Gericht",
            "Gesamtschule",
            "Geschäft",
            "Geschenk",
            "Geschirr",
            "Geschwister",
            "Gesicht",
            "Gespräch",
            "Gesundheit",
            "Getränk",
            "Gewicht",
            "Gewitter",
            "Glück",
            "Glückwunsch",
            "Glas",
            "Gleis",
            "Goethe-Institut",
            "Größe",
            "Die Grenze",
            "Grippe",
            "Großeltern",
            "Großmutter",
            "Großvater",
            "Gruß",
            "Grundschule",
            "Gruppe",
            "Guthaben",
            "Gymnasium",
            "Hähnchen",
            "Haar",
            "Halbpension",
            "Halle",
            "Hals",
            "Haltestelle",
            "Hand",
            "Handtuch",
            "Handy",
            "Haus",
            "Hausaufgabe",
            "Hausfrau",
            "Haushalt",
            "Hausmann",
            "Heimat",
            "Heizung",
            "Hemd",
            "Herbst",
            "Herd",
            "Herr",
            "Herz",
            "Hilfe",
            "Hobby",
            "Holz",
            "Hose",
            "Hund",
            "Hunger",
            "Idee",
            "Import",
            "Industrie",
            "Information",
            "Inhalt",
            "Internet",
            "Jacke",
            "Jahr",
            "Januar",
            "Job",
            "Jugendherberge",
            "Jugendliche",
            "Juli",
            "Junge",
            "Juni",
            "Käse",
            "Körper",
            "Küche",
            "Kühlschrank",
            "Kündigung",
            "Kaffee",
            "Kalender",
            "Kamera",
            "Kanne",
            "Karte",
            "Kartoffel",
            "Kasse",
            "Kassette",
            "Kassettenrecorder",
            "Katze",
            "Keller",
            "Kellner",
            "Kenntnisse",
            "Kennzeichen",
            "Kette",
            "Kfz",
            "Kind",
            "Kindergarten",
            "Kinderwagen",
            "Kino",
            "Kiosk",
            "Kirche",
            "Klasse",
            "Kleid",
            "Kleidung",
            "Kneipe",
            "Koffer",
            "Kollege",
            "Kollegin",
            "Konsulat",
            "Kontakt",
            "Konto",
            "Kontrolle",
            "Konzert",
            "Kopf",
            "Kosmetik",
            "Krankenkasse",
            "Krankheit",
            "Kredit",
            "Kreditkarte",
            "Kreis",
            "Kreuzung",
            "Kuchen",
            "Kugelschreiber",
            "Kunde",
            "Kundin",
            "Kurs",
            "Löffel",
            "Lösung",
            "Laden",
            "Lager",
            "Lampe",
            "Land",
            "Landschaft",
            "Leben",
            "Lebensmittel",
            "Leid",
            "Lehre",
            "Lehrer",
            "Lehrerin",
            "Leute",
            "Licht",
            "Lied",
            "Lkw",
            "Loch",
            "Lohn",
            "Lokal",
            "Luft",
            "Lust",
            "Mädchen",
            "März",
            "Möbel",
            "Müll",
            "Mülltonne",
            "Magen",
            "Mai",
            "Mal",
            "Mann",
            "Mantel",
            "Markt",
            "Maschine",
            "Material",
            "Mechaniker",
            "Medikament",
            "Meer",
            "Mehrwertsteuer",
            "Meinung",
            "Menge",
            "Mensch",
            "Messer",
            "Metall",
            "Miete",
            "Milch",
            "Minute",
            "Mittag",
            "Mitte",
            "Mitteilung",
            "Mittel",
            "Mittelschule",
            "Mittwoch",
            "Mode",
            "Moment",
            "Monat",
            "Montag",
            "Morgen",
            "Motor",
            "Mund",
            "Museum",
            "Musik",
            "Mutter",
            "Nähe",
            "Nachbar",
            "Nachbarin",
            "Nachmittag",
            "Nachrichten",
            "Nacht",
            "Name",
            "Natur",
            "Nebel",
            "Norden",
            "Notarzt",
            "Note",
            "Notfall",
            "Notiz",
            "November",
            "Nudel",
            "Nummer",
            "Ober",
            "Obst",
            "Oktober",
            "Oma",
            "Opa",
            "Operation",
            "Orange",
            "Ordnung",
            "Ort",
            "Osten",
            "Öl",
            "Päckchen",
            "Paket",
            "Panne",
            "Papier",
            "Papiere",
            "Parfüm",
            "Park",
            "Partei",
            "Partner",
            "Partnerin",
            "Party",
            "Pass",
            "Pause",
            "Pension",
            "Pkw",
            "Plan",
            "Plastik",
            "Platz",
            "Polizei",
            "Pommes frites",
            "Portion",
            "Post",
            "Postleitzahl",
            "Prüfung",
            "Praktikum",
            "Praxis",
            "Preis",
            "Problem",
            "Das Produkt",
            "Programm",
            "Prospekt",
            "Pullover",
            "Qualität",
            "Quittung",
            "Rücken",
            "Rabatt",
            "Radio",
            "Rathaus",
            "Raucher",
            "Raucherin",
            "Raum",
            "Realschule",
            "Rechnung",
            "Regen",
            "Reifen",
            "Reinigung",
            "Reis",
            "Reise",
            "Reisebüro",
            "Reiseführer",
            "Reparatur",
            "Restaurant",
            "Rezept",
            "Rezeption",
            "Rind",
            "Rock",
            "Rose",
            "Rundgang",
            "Süden",
            "S-Bahn",
            "Sache",
            "Saft",
            "Salat",
            "Salz",
            "Samstag/Sonnabend",
            "Satz",
            "Schüler",
            "Schülerin",
            "Schalter",
            "Scheckkarte",
            "Schiff",
            "Schild",
            "Schinken",
            "Schirm",
            "Schlüssel",
            "Schloss",
            "Schluss",
            "Schmerzen",
            "Schnee",
            "Schnupfen",
            "Schokolade",
            "Schrank",
            "Schuh",
            "Schule",
            "Schwein",
            "Schwester",
            "Schwimmbad",
            "See",
            "Sehenswürdigkeit",
            "Seife",
            "Sekretärin",
            "Sekunde",
            "Sendung",
            "Senioren",
            "September",
            "Service",
            "Sessel",
            "Sofa",
            "Sohn",
            "Sommer",
            "Sonderangebot",
            "Sonne",
            "Sonntag",
            "Sorge",
            "Spülmaschine",
            "Spaß",
            "Spaziergang",
            "Speisekarte",
            "Spielplatz",
            "Sprache",
            "Sprachschule",
            "Sprechstunde",
            "Stück",
            "Stadt",
            "Standesamt",
            "Stempel",
            "Steuer",
            "Stock",
            "Stoff",
            "Straße",
            "Straßenbahn",
            "Strand",
            "Streichholz",
            "Strom",
            "Student",
            "Studentin",
            "Studium",
            "Stuhl",
            "Stunde",
            "Supermarkt",
            "Suppe",
            "Tür",
            "Tüte",
            "Tag",
            "Tankstelle",
            "Tasche",
            "Tasse",
            "Taxi",
            "Der Tee",
            "Teil",
            "Telefon",
            "Telefonbuch",
            "Teller",
            "Teppich",
            "Termin",
            "Test",
            "Text",
            "Theater",
            "Thema",
            "Ticket",
            "Tier",
            "Tipp",
            "Tisch",
            "Tochter",
            "Toilette",
            "Tomate",
            "Topf",
            "Tourist",
            "Treppe",
            "Trinkgeld",
            "Turm",
            "U-Bahn",
            "Uhr",
            "Unfall",
            "Universität",
            "Unterhaltung",
            "Unterkunft",
            "Unterricht",
            "Unterschied",
            "Unterschrift",
            "Untersuchung",
            "Urlaub",
            "Übernachtung",
            "Vater",
            "Verbindung",
            "Verein",
            "Verkäufer",
            "Verkäuferin",
            "Verkehr",
            "Vermieter",
            "Versicherung",
            "Verspätung",
            "Vertrag",
            "Video",
            "Vogel",
            "Volksschule",
            "Vormittag",
            "Vorname",
            "Vorsicht",
            "Vorwahl",
            "Wäsche",
            "Wagen",
            "Wald",
            "Wasser",
            "Weg",
            "Wein",
            "Welt",
            "Werkstatt",
            "Werkzeug",
            "Westen",
            "Wetter",
            "Wiederhören",
            "Wiedersehen",
            "Wind",
            "Winter",
            "Wirtschaft",
            "Woche",
            "Wochenende",
            "Wochentag",
            "Wohnung",
            "Wolke",
            "Wort",
            "Wunsch",
            "Wurst",
            "Zahl",
            "Zahn",
            "Zeit",
            "Zeitschrift",
            "Zeitung",
            "Zentrum",
            "Zettel",
            "Zeugnis",
            "Zigarette",
            "Zimmer",
            "Zitrone",
            "Zoll",
            "Zucker",
            "Zug"
        };

        public static string GetGallowWord()
        {
            var num = random.Next(0, GallowWords.Count - 1);
            return GallowWords[num];
        }

        public static async Task<string> ResolveURL(string url, IConfiguration Configuration)
        {
            if (url.Contains("twitch.tv"))
            {
                if ((url.ToLower().StartsWith("http") && url.Count(x => x == '/') == 3) || url.Count(x => x == '/') == 1)
                    return url.Split('/').Last();
                else
                    return "v" + url.Split('/').Last();
            }
            string title = "";
            title = (await NoEmbedYTApi(url)).Title;

            if (title == null || title.Length == 0)
            {
                try
                {
                    var webGet = new HtmlWeb();
                    var document = webGet.Load(url);
                    title = document.DocumentNode.SelectSingleNode("html/head/title").InnerText;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }

            if (title.Length == 0)
            {
                try
                {
                    string videokey = GetYTVideoKey(url);
                    string infoUrl = "http://youtube.com/get_video_info?video_id=" + videokey;
                    using (WebClient client = new WebClient())
                    {
                        string source = "";
                        int i = 0;
                        while ((title.Length == 0 || title.ToLower().Trim() == "youtube") && i < 10)
                        {
                            source = client.DownloadString(infoUrl);
                            if (source.Length > 0)
                            {
                                List<string> attributes = source.Split('&').Select(x => HttpUtility.UrlDecode(x)).ToList();
                                int idx = attributes.FindIndex(x => x.StartsWith("player_response="));
                                if (idx != -1)
                                {
                                    YtVideoInfo videoInfo = new YtVideoInfo().FromJson(attributes[idx].Split(new[] { '=' }, 2)[1]);
                                    return videoInfo.VideoDetails.Title + " - " + videoInfo.VideoDetails.Author;
                                }
                            }
                            await Task.Delay(50);
                            i++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
            if (title.Length == 0)
            {
                title = await YTApiInfo(url, Configuration);
            }
            return title;
        }

        public static string GetYTVideoKey(string url)
        {
            Uri uri = new Uri(url);
            string videokey = System.Web.HttpUtility.ParseQueryString(uri.Query).Get("v");
            if (videokey == null)
                videokey = System.Web.HttpUtility.ParseQueryString(uri.Query).Get("list");
            return videokey;
        }

        public async static Task<YTApiNoEmbed> NoEmbedYTApi(string url)
        {
            YTApiNoEmbed apiResult = new YTApiNoEmbed();
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://noembed.com/embed?url=" + url);
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync())
                using (System.IO.Stream stream = response.GetResponseStream())
                using (System.IO.StreamReader reader = new System.IO.StreamReader(stream))
                {
                    apiResult = new YTApiNoEmbed().FromJson(await reader.ReadToEndAsync());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return apiResult;
        }

        public static async Task<string> YTApiInfo(string url, IConfiguration Configuration)
        {
            try
            {
                string title = "";
                var section = Configuration.GetSection("YTKey");
                string key = section.Value;
                string videokey = GetYTVideoKey(url);
                string Url = "https://www.googleapis.com/youtube/v3/videos?part=snippet&id=" + videokey + "&key=" + key;
                Ytapi apiResult;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Url);
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync())
                using (System.IO.Stream stream = response.GetResponseStream())
                using (System.IO.StreamReader reader = new System.IO.StreamReader(stream))
                {
                    apiResult = new Ytapi().FromJson(await reader.ReadToEndAsync());
                }
                if (apiResult != null && apiResult.Items.Count > 0)
                    title = apiResult.Items.First().Snippet.Title + " - " + apiResult.Items.First().Snippet.ChannelTitle;
                return title;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return "";
            }
        }

        public static async Task<(string title, string source)> ResolveTitle(string url, int maxTries)
        {
            try
            {
                string title = "";
                using (WebClient client = new WebClient())
                {
                    string source = "";
                    int i = 0;
                    while ((title.Length == 0 || title.ToLower().Trim() == "youtube") && i < maxTries)
                    {
                        source = client.DownloadString(url);
                        title = System.Text.RegularExpressions.Regex.Match(source, @"\<title\b[^>]*\>\s*(?<Title>[\s\S]*?)\</title\>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups["Title"].Value;
                        await Task.Delay(50);
                        i++;
                    }
                    if (title.Length == 0)
                        title = "External source";
                    return (title, source);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return ("External source", "");
            }
        }
    }
}
