// Copyright (c) 2010, Douglas V. Johnston <doug.johnston@gmail.com>
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following condition are met:
//
//    * Redistributions of source code must retain the above copyright notice,
//      this list of conditions and the following disclaimer.
//    * Redistributions in binary form must reproduce the above copyright
//      notice, this list of conditions and the following disclaimer in the
//      documentation and/or other materials provided with the distribution.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
// ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE
// LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
// CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
// SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
// INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
// CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
// ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
// POSSIBILITY OF SUCH DAMAGE.
using System;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using System.Xml.Schema;
using Evernote.EDAM.NoteStore;
using Evernote.EDAM.Type;
using Evernote.EDAM.UserStore;
using Thrift.Protocol;
using Thrift.Transport;
using Notebook=Evernote.EDAM.Type.Notebook;

//Global TODOs:
// TODO - implement a locking mechanism
// TODO - make the commits transactional
// TODO - solve GUID problem
// TODO - Convert Tomboy->EDAM-XML and EDAM-XML->tomboy, instead of stripping tags (take advantage of NoteConvert class?)
// TODO - StripEvernoteHeadersFromNoteContent is terrible. Fix it.
// TODO - properly try/catch all the evernote calls. They throw lots of different exceptions
// TODO - return errors to the user, don't silently fail


namespace Tomboy.Sync
{

    public class EvernoteSyncServer : SyncServer
    {
        // ************************************************
        // ***** YOU MUST REPLACE THESE WITH YOUR OWN *****
        // ************************************************
        // SEE: http://www.evernote.com/about/developer/api/
        const string ConsumerKey = EvernoteKeys.ConsumerKey;
		const string ConsumerSecret = EvernoteKeys.ConsumerSecret;
		const string EdamBaseUrl = "https://sandbox.evernote.com";

        // If using Mono, see http://www.mono-project.com/FAQ:_Security
        const string UserStoreUrl = EdamBaseUrl + "/edam/user";
        private readonly UserStore.Client _userStore;
        private NoteStore.Client _noteStore;
        private string _authToken;
        private Evernote.EDAM.Type.Notebook _tomboyNotebook;
        private readonly DateTime Epoch = new DateTime(1970,1,1,0,0,0);
        private SyncLockInfo _syncLock = new SyncLockInfo();
        const string TomboyHeader = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<note version=\"" + NoteArchiver.CURRENT_VERSION +
                            "\" xmlns:link=\"http://beatniksoftware.com/tomboy/link\" xmlns:size=\"http://beatniksoftware.com/tomboy/size\"" +
                            " xmlns=\"http://beatniksoftware.com/tomboy\">";
        public EvernoteSyncServer()
        {
            TTransport userStoreTransport = new THttpClient(UserStoreUrl);
            TProtocol userStoreProtocol = new TBinaryProtocol(userStoreTransport);
            _userStore = new UserStore.Client(userStoreProtocol);
        }

        private static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors != SslPolicyErrors.None)
            {
                Logger.Warn("[Evernote] SSL Policy Error: Certificate " + certificate.Issuer + " contains error " + sslPolicyErrors);
            }
            return true;
        }

        public bool BeginSyncTransaction()
        {
            if (ConsumerKey.Equals("your_key_here"))
            {
                throw new Exception("You need to Update the Evernote ConsumerKey!! Open up the EvernoteSyncServer.cs file and look at it!");
            }
			//ServicePointManager.CertificatePolicy = new CertificateManager ();
            ServicePointManager.ServerCertificateValidationCallback += ValidateServerCertificate;
            bool versionOK =
                _userStore.checkVersion("Tomboy.EvernoteSyncAddin",
                                        Evernote.EDAM.UserStore.Constants.EDAM_VERSION_MAJOR,
                                        Evernote.EDAM.UserStore.Constants.EDAM_VERSION_MINOR);

            if (!versionOK)
            {
                Logger.Error("[Evernote] EDAM protocol version not up to date: " +
                             Evernote.EDAM.UserStore.Constants.EDAM_VERSION_MAJOR + "." +
                             Evernote.EDAM.UserStore.Constants.EDAM_VERSION_MINOR);
                return false;
            }
            string username;
            string password;
            if (!EvernoteSyncServiceAddin.GetConfigSettings(out username, out password))
            {
                return false;
            }
            AuthenticationResult authResult =
                _userStore.authenticate(username, password, ConsumerKey, ConsumerSecret);
            User user = authResult.User;
            _authToken = authResult.AuthenticationToken;
            Logger.Debug("[Evernote] Authentication successful for: " + user.Username);
            Logger.Debug("[Evernote] Authentication token = " + _authToken);


            String noteStoreUrl = EdamBaseUrl + "/edam/note/" + user.ShardId;
            TTransport noteStoreTransport = new THttpClient(noteStoreUrl);
            TProtocol noteStoreProtocol = new TBinaryProtocol(noteStoreTransport);
            _noteStore = new NoteStore.Client(noteStoreProtocol);

            //TODO - check if the user has a 'Tomboy' Notebook. If not, add one
            bool foundTomboyNotebook = false;
            List<Notebook> notebooks = _noteStore.listNotebooks(_authToken);
            foreach (Notebook notebook in notebooks)
            {
                if (notebook.Name.ToLowerInvariant().Trim().Equals("tomboy"))
                {
                    foundTomboyNotebook = true;
                    _tomboyNotebook = notebook;
                }
            }

            if (!foundTomboyNotebook)
            {
                // no tomboy notebook found, so try to add one
                _tomboyNotebook = new Notebook();
                _tomboyNotebook.Name = "Tomboy";
                _tomboyNotebook.DefaultNotebook = false;
                _tomboyNotebook.Guid = new Guid().ToString();
                _tomboyNotebook = _noteStore.createNotebook(_authToken, _tomboyNotebook);
                if (_tomboyNotebook == null)
                {
                    Logger.Error("[Evernote] Could not create 'Tomboy' notebook in evernote");
                    return false;
                }
                Console.WriteLine("[Evernote] New Tomboy notebook with guid : " + _tomboyNotebook.Guid);
            }

            return true;
        }

        public bool CommitSyncTransaction()
        {
            //TODO - actually sync
            //TODO - Success! Close down everything
            return true;
        }

        public bool CancelSyncTransaction()
        {
            //TODO - close down everything
            return true;
        }

        private static string GetCorrectGuid(Evernote.EDAM.Type.Note note)
        {
            if (note.Attributes.SourceApplication != null && note.Attributes.SourceApplication.ToLowerInvariant() == "tomboy"
                && !string.IsNullOrEmpty(note.Attributes.Source))
            {
                //it looks like a GUID, and talks like a GUID, so assume it's a GUID
                return (note.Attributes.Source);
            }
            else
            {
                return (note.Guid);
            }
        }

        public IList<string> GetAllNoteUUIDs()
        {
            NoteFilter filter = new NoteFilter();
            filter.NotebookGuid = _tomboyNotebook.Guid;
            NoteList notes = _noteStore.findNotes(_authToken, filter, 0, Evernote.EDAM.Limits.Constants.EDAM_USER_NOTES_MAX);
            return notes.Notes.Select(GetCorrectGuid).ToList();
        }

        public IDictionary<string, NoteUpdate> GetNoteUpdatesSince(int revision)
        {
            IDictionary<string, NoteUpdate> updatedNotes = new Dictionary<string, NoteUpdate>();
            Evernote.EDAM.NoteStore.SyncState syncState =  _noteStore.getSyncState(_authToken);
            // see if anything has changed
            // TODO - this check might be redundant, as Tomboy calls LatestRevision() beforehand
            if (syncState.UpdateCount <= revision)
            {
                return updatedNotes;
            }
            if (revision < 0) revision = 0;
            //if we got this far, then Something has changed

            Evernote.EDAM.NoteStore.SyncChunk syncChunk;
            try
            {
                syncChunk = _noteStore.getSyncChunk(_authToken, revision, Int32.MaxValue, false);
            }
            catch (Exception e)
            {
                Logger.Error("[Evernote] Failure in getSyncChunk: " + e);
                return updatedNotes;
            }
            if (syncChunk.Notes == null)
			{
				return updatedNotes;
			}
                //every note we have should be new or updated, so tell Tomboy about it
            foreach (Evernote.EDAM.Type.Note note in syncChunk.Notes)
            {
                if (note.NotebookGuid != _tomboyNotebook.Guid)
                {
                    continue;
                }
                string content = "";
                try
                {
                    content = CreateTomboyNoteContent(note);
                }
                catch (XmlSchemaValidationException e)
                {
                    content = "Evernote had invalid XML";
                }
                catch (Exception e)
                {
                    Logger.Error("[Evernote] Unknown error creating Tomboy Note from Evernote:" + e + "\nEvernote: " + note);
                }
                string guid = GetCorrectGuid(note);
                NoteUpdate update = new NoteUpdate(content, note.Title, guid, note.UpdateSequenceNum);
                updatedNotes.Add(note.Guid, update);
            }
            return updatedNotes;
        }

        // Evernote has a bunch of junk in it's xml tomboy doesn't care about, and tomboy
        // also requires some specific tags. So strip out evernote's and add tomboys
        // TODO - Really, Really need to convert the html-like tags, not just strip them
        // TODO -  so that we can keep things like bulleted lists, font sizes, etc
        public string CreateTomboyNoteContent(Evernote.EDAM.Type.Note note)
        {
            string  evernoteContent;
            try
            {
                evernoteContent = _noteStore.getNoteContent(_authToken, note.Guid);
            }
            catch (Exception e)
            {
                evernoteContent = "Could not retrieve Evernote Content";
            }
            ExportManager exportManager = new ExportManager("EvernoteToTomboy.xslt");
            string tomboyContent;
            try
            {
                tomboyContent = exportManager.ApplyXSL(evernoteContent, note.Title, null, ValidationType.DTD);
            }
            catch (Exception e)
            {
                tomboyContent = "Evernote Export failed." +
                    " Please report a bug at http://bugzilla.gnome.org with the following: (strip out confidential information)" +
                    e + "\n" + note;
            }

            string content = TomboyHeader + "\n" + "<title>" + note.Title + "</title>" +
                             "<text xml:space=\"preserve\"><note-content version=\"0.1\">" + "\n";
            content += tomboyContent;
            content += "</note-content></text>\n";
            DateTime date;
            if (note.Updated > DateTime.MinValue.Ticks && note.Updated < DateTime.MaxValue.Ticks)
            {
                date = Epoch.Add(TimeSpan.FromTicks(note.Updated*10000));
            } else
            {
                date = DateTime.Now;
            }
            content += "<last-change-date>" + date + "</last-change-date>\n";
            content += "<last-metadata-change-date>" + date + "</last-metadata-change-date>\n";
            if (note.Created > DateTime.MinValue.Ticks && note.Created < DateTime.MaxValue.Ticks)
            {
                date = Epoch.Add(TimeSpan.FromTicks(note.Created * 10000));
            }
            content += "<create-date>" + date + "</create-date>\n";

            content += "</note>";
            return content;
        }

        public void DeleteNotes(IList<string> deletedNoteUUIDs)
        {
            NoteFilter noteFilter = new NoteFilter();
            noteFilter.NotebookGuid = _tomboyNotebook.Guid;
            NoteList evernoteList = _noteStore.findNotes(_authToken, noteFilter, 0,
                                                         Evernote.EDAM.Limits.Constants.EDAM_USER_NOTES_MAX);

            foreach (string guid in deletedNoteUUIDs)
            {
                bool foundNote = false;
                foreach (Evernote.EDAM.Type.Note evernote in evernoteList.Notes)
                {
                    if (GetCorrectGuid(evernote) == guid)
                    {
                        foundNote = true;
                        evernote.Deleted = (long)DateTime.Now.Subtract(Epoch).TotalMilliseconds;
                        _noteStore.updateNote(_authToken, evernote);
                    }
                }
                if (!foundNote)
                {
                    Logger.Error("[Evernote] Could not find note " + guid + " to delete.");
                }

            }
        }

        public void UploadNotes(IList<Note> notes)
        {
            //TODO - this could take a long time, think of a better way to do this
            //TODO - an alternative is to just try to Update and evernote (without checking whether it exists),
            //TODO -  but the _noteStore.updateNote function throws an Exception on error, instead of returning false
            //TODO -  and using exceptions for process control is costly.

            NoteFilter noteFilter= new NoteFilter();
            noteFilter.NotebookGuid = _tomboyNotebook.Guid;
            NoteList evernoteList = _noteStore.findNotes(_authToken, noteFilter, 0, Evernote.EDAM.Limits.Constants.EDAM_USER_NOTES_MAX);
            Logger.Debug("[Evernote] Uploading " + notes.Count + " notes");

            foreach (Note note in notes)
            {
                bool foundNote = false;
				Evernote.EDAM.Type.Note enote = null;
                foreach (Evernote.EDAM.Type.Note evernote in evernoteList.Notes)
                {
                    if (GetCorrectGuid(evernote) == note.Id)
                    {
                        foundNote = true;
						enote = evernote;
                        break;
                    }
                }
                if (foundNote)
                {
                    if (!UpdateEvernote(enote, note))
                    {
                        Logger.Error("[Evernote] Could not update Evernote: " + note);
                        throw new TomboySyncException("Could not Update Evernote");
                    }
                }
                else
                {
                    //does not exist, so create a new note.
                    if (!CreateNewEvernote(note))
                    {
                        Logger.Debug("[Evernote] Problem creating note with id" + note.Id);
                        throw new TomboySyncException("Couldn't create Evernote");
                    }
                }
            }
        }

        private bool CreateNewEvernote(Note note)
        {
            Evernote.EDAM.Type.Note newNote = new Evernote.EDAM.Type.Note();
            newNote = FillEvernote(note, newNote);
            try
            {
                _noteStore.createNote(_authToken, newNote);
            }
            catch (Exception e)
            {
                Logger.Error("[Evernote] Error Creating new Evernote :" + e + "\n EverNote: " + newNote + "\nTomboyNote: " + note);
                return false;
            }
            return true;
        }
        private static void ValidateXML(string xmlString)
        {
            StringReader sr = new StringReader(xmlString);
            XmlReaderSettings settings = new XmlReaderSettings();
            settings.ValidationType = ValidationType.DTD;
            settings.ProhibitDtd = false;
            settings.ValidationEventHandler += ExportManager.MyValidationEventHandler;
            XmlReader reader = XmlReader.Create(sr, settings);
            while (reader.Read()) ;
        }

        private Evernote.EDAM.Type.Note FillEvernote(Note tomboynote, Evernote.EDAM.Type.Note evernote)
        {
            Logger.Debug("[Evernote] Creating new Evernote from tomboy:" + tomboynote.Id.ToString());
			if (evernote.Attributes == null) {
				evernote.Attributes = new NoteAttributes();
			}
            evernote.Attributes.SourceApplication = "tomboy"; //this note came from tomboy. This is read later to match guids
            evernote.Attributes.Source = tomboynote.Id;

            ExportManager exportManager = new ExportManager("TomboyToEvernote.xslt");
            string mycontent = exportManager.ApplyXSL(tomboynote);

            string content = mycontent;
            evernote.Content += "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                                "<!DOCTYPE en-note SYSTEM \"http://xml.evernote.com/pub/enml.dtd\">";
            evernote.Content +=  content;
            try
            {
                ValidateXML(evernote.Content);
            }
            catch (Exception e)
            {
                Logger.Error("[Evernote] Could not validate XML: " + evernote.Content);
            }

            evernote.Title = tomboynote.Title;
            if (tomboynote.Tags.Count > 0) {
                evernote.TagNames = new List<string>();
	            foreach (Tag tag in tomboynote.Tags)
	            {
	                evernote.TagNames.Add(tag.Name);
	            }
			}
            evernote.NotebookGuid = _tomboyNotebook.Guid;
            evernote.Created = (long)tomboynote.CreateDate.Subtract(Epoch).TotalMilliseconds;
            evernote.Updated = (long)tomboynote.ChangeDate.Subtract(Epoch).TotalMilliseconds;
            return evernote;
        }

        private bool UpdateEvernote(Evernote.EDAM.Type.Note evernote,  Note tomboynote)
        {
			Logger.Debug("[Evernote] Updating evernote: " + evernote.Guid);
            try
            {
                evernote = FillEvernote(tomboynote, evernote);
                _noteStore.updateNote(_authToken, evernote);
            }
            catch (Exception e)
            {
				Logger.Warn("[Evernote] Could not Update note: " + e + "EverNote: " + evernote + "\nTomboyNote: " + tomboynote);
                return false;
            }

            return true;
        }

        public int LatestRevision
        {
            get
            {
                NoteFilter filter = new NoteFilter();
                filter.NotebookGuid = _tomboyNotebook.Guid;
                NoteList noteList = _noteStore.findNotes(_authToken, filter, 0, Evernote.EDAM.Limits.Constants.EDAM_USER_NOTES_MAX);
                int highestRevision = 0;
                foreach (Evernote.EDAM.Type.Note note in noteList.Notes)
                {
                    if (note.UpdateSequenceNum > highestRevision)
                    {
                        highestRevision = note.UpdateSequenceNum;
                    }
                }
                return highestRevision;
            }
        }

        //TODO - anything?
        public SyncLockInfo CurrentSyncLock
        {
            get { return null; }
        }

        public string Id
        {
            get { return "Evernote-0001"; }
        }
    }
}
