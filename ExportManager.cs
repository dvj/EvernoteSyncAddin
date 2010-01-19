using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Xsl;

namespace Tomboy
{
    interface INoteExport
    {

    }
	public class ExportManager
	{
        private readonly XslCompiledTransform _xsl;
        public ExportManager(string xslLocation)
        {
            _xsl = new XslCompiledTransform();
            if (!LoadXSL(xslLocation))
            {
                throw new Exception("Could not load XSL:" + xslLocation + ". ");
            }
        }
        public bool LoadXSL(string xslLocation)
        {
            if (File.Exists(xslLocation))
            {
                Logger.Debug("[Evernote] Using user-custom {0} file.", xslLocation);
                _xsl.Load(xslLocation);
            }
            else
            {
                //TODO - could do fancy stuff with embedded assembly. Probably not worth it
                Assembly asm = Assembly.GetExecutingAssembly();
                string[] names = asm.GetManifestResourceNames();
                string asmDir = System.IO.Path.GetDirectoryName(asm.Location);
                string xslLocation2 = Path.Combine(asmDir, xslLocation);
                if (File.Exists(xslLocation2))
                {
                    Logger.Debug("[Evernote] Using user-custom {0} file.", xslLocation);
                    _xsl.Load(xslLocation);
                }
                else
                {
                    Stream resource = asm.GetManifestResourceStream(xslLocation);
                    if (resource != null)
                    {
                        XmlTextReader reader = new XmlTextReader(resource);
                        Logger.Debug("[Evernote] Using user-custom {0} file.", xslLocation);
                        _xsl.Load(reader, null, null);
                        resource.Close();
                        return true;
                    }                    
                    Logger.Error("[Evernote] Unable to find XSL export template '{0}'.", xslLocation);
                    return false;
                }
            }
            return true;
        }
        public string ApplyXSL(string content, string title, XmlResolver resolver, ValidationType validate)
        {
            StringReader reader = new StringReader(content);
            XmlReaderSettings rsettings = new XmlReaderSettings();
            if (validate != ValidationType.None)
            {
                rsettings.ValidationType = validate;
                if (validate == ValidationType.DTD)
                {
                    rsettings.ProhibitDtd = false;
                }
                rsettings.ValidationEventHandler += MyValidationEventHandler;
            }
            XmlReader doc = XmlReader.Create(reader, rsettings);
            XsltArgumentList args = new XsltArgumentList();
            args.AddParam("root-note", "", title);

            args.AddExtensionObject("http://beatniksoftware.com/tomboy",
                                    new TransformExtension());

            StringWriter outWriter = new StringWriter();
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.ConformanceLevel = ConformanceLevel.Auto;
            settings.OmitXmlDeclaration = true;

            XmlWriter writer = XmlWriter.Create(outWriter, settings);

            if (writer == null)
            {
                Logger.Error("XmlWriter was null");
                throw new NullReferenceException("xmlWriter was null");
            }
            _xsl.Transform(doc, args, writer, resolver);

            doc.Close();
            outWriter.Close();
            return outWriter.ToString();


        }

	    public string ApplyXSL(Note note, ValidationType validationType = ValidationType.None)
        {
            StringWriter sWriter = new StringWriter();
            NoteArchiver.Write(sWriter, note.Data);
            sWriter.Close();
            NoteNameResolver resolver = new NoteNameResolver(note.Manager, note);
            return ApplyXSL(sWriter.ToString(), note.Title, resolver, validationType);
        }

        // this gets called if we are validating our XML, which is a good idea in general.
        public static void MyValidationEventHandler(object sender,
                                                  ValidationEventArgs args)
        {
            Logger.Error("XML validation failed: [" +args.Severity + "] " + args.Message +"\n" + args.Exception);
            throw new XmlSchemaValidationException(args.Message, args.Exception);
        }
	}

    public class NoteNameResolver : XmlResolver
    {
        readonly NoteManager _manager;

        // Use this list to keep track of notes that have already been
        // resolved.
        readonly List<string> _resolvedNotes;

        public NoteNameResolver(NoteManager manager, Note originNote)
        {
            _manager = manager;

            _resolvedNotes = new List<string>();

            // Add the original note to the list of resolved notes
            // so it won't be included again.
            _resolvedNotes.Add(originNote.Title.ToLower());
        }

        public override System.Net.ICredentials Credentials
        {
            set { }
        }

        public override object GetEntity(Uri absolute_uri, string role, Type of_object_to_return)
        {
            Note note = _manager.FindByUri(absolute_uri.ToString());
            if (note == null)
                return null;

            StringWriter writer = new StringWriter();
            NoteArchiver.Write(writer, note.Data);
            Stream stream = WriterToStream(writer);
            writer.Close();

            return stream;
        }

        // Using UTF-16 does not work - the document is not processed.
        // Also, the byte order marker (BOM in short, locate at U+FEFF,
        // 0xef 0xbb 0xbf in UTF-8) must be included, otherwise parsing fails
        // as well. This way the buffer contains an exact representation of
        // the on-disk representation of notes.
        //
        // See http://en.wikipedia.org/wiki/Byte_Order_Mark for more
        // information about the BOM.
        static MemoryStream WriterToStream(TextWriter writer)
        {
            UTF8Encoding encoding = new UTF8Encoding();
            string s = writer.ToString();
            int bytesRequired = 3 + encoding.GetByteCount(s);
            byte[] buffer = new byte[bytesRequired];
            buffer[0] = 0xef;
            buffer[1] = 0xbb;
            buffer[2] = 0xbf;
            encoding.GetBytes(s, 0, s.Length, buffer, 3);
            return new MemoryStream(buffer);
        }

        public override Uri ResolveUri(Uri baseUri, string relativeUri)
        {
            string noteTitleLowered = relativeUri.ToLower();
            if (_resolvedNotes.Contains(noteTitleLowered))
            {
                return new Uri("");
            }

            Note note = _manager.Find(relativeUri);
            if (note != null)
            {
                _resolvedNotes.Add(noteTitleLowered);
                return new Uri(note.Uri);
            }

            return new Uri("");
        }
    }

    public class TransformExtension
    {
        public String ToNMToken(string s)
        {
            return Regex.Replace(s, @"\W", "-").ToLowerInvariant();
        }
    }

}
