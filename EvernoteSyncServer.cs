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
using System.Security;
using System.Net;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Evernote.EDAM.NoteStore;
using Evernote.EDAM.Type;
using Evernote.EDAM.UserStore;
using Thrift.Protocol;
using Thrift.Transport;
using Notebook=Evernote.EDAM.Type.Notebook;
using Tomboy.WebSync.Api;

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
        public EvernoteSyncServer()
        {
            TTransport userStoreTransport = new THttpClient(UserStoreUrl);
            TProtocol userStoreProtocol = new TBinaryProtocol(userStoreTransport);
            _userStore = new UserStore.Client(userStoreProtocol);   
        }

        public bool BeginSyncTransaction()
        {
            if (ConsumerKey.Equals("your_key_here"))
            {
                throw new Exception("You need to Update the Evernote ConsumerKey!! Open up the EvernoteSyncServer.cs file and look at it!");
            }
			ServicePointManager.CertificatePolicy = new CertificateManager ();
            bool versionOK =
                _userStore.checkVersion("Tomboy.EvernoteSyncAddin",
                                        Evernote.EDAM.UserStore.Constants.EDAM_VERSION_MAJOR,
                                        Evernote.EDAM.UserStore.Constants.EDAM_VERSION_MINOR);
            
            if (!versionOK)
            {
                Logger.Error("EDAM protocol version not up to date: " +
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
            Logger.Debug("Authentication successful for: " + user.Username);
            Logger.Debug("Authentication token = " + _authToken);

            
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
                Console.WriteLine("Current Guid: " + _tomboyNotebook.Guid);
                _tomboyNotebook = _noteStore.createNotebook(_authToken, _tomboyNotebook);
                if (_tomboyNotebook == null)
                {
                    Logger.Error("Could not create 'Tomboy' notebook in evernote");
                    return false;
                }
                Console.WriteLine("New Guid    : " + _tomboyNotebook.Guid);
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

        public string GetCorrectGuid(Evernote.EDAM.Type.Note note)
        {
            if (note.Attributes.SourceApplication != null && note.Attributes.SourceApplication.ToLowerInvariant() == "tomboy"
                && !string.IsNullOrEmpty(note.Attributes.Source))
            {
                //if looks like a GUID, and talks like a GUID, so assume it's a GUID
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
            IList<string> uuids = new List<string>();
            foreach (Evernote.EDAM.Type.Note note in notes.Notes)
            {
                uuids.Add(GetCorrectGuid(note));
            }
            return uuids;
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
                Logger.Error("Failure in getSyncChunk: " + e);
                return updatedNotes;
            }
            if (syncChunk.Notes == null)
			{
				return updatedNotes;
			}
            //try
            {
                //every note we have should be new or updated, so tell Tomboy about it
                foreach (Evernote.EDAM.Type.Note note in syncChunk.Notes)
                {
                    if (note.NotebookGuid != _tomboyNotebook.Guid) {continue;}                    
                    string content = CreateTomboyNoteContent(note);
                    string guid = GetCorrectGuid(note);
                    NoteUpdate update = new NoteUpdate(content, note.Title, guid, note.UpdateSequenceNum);
                    updatedNotes.Add(note.Guid, update);
                }
            }
            //catch( Exception e)
            //{
            //    //TODO - fix this catch and do something useful
            //    Logger.Error("Error in getting note content:" + e);
            //}
            return updatedNotes;
        }

        // Evernote has a bunch of junk in it's xml tomboy doesn't care about, and tomboy
        // also requires some specific tags. So strip out evernote's and add tomboys
        // TODO - Really, Really need to convert the html-like tags, not just strip them
        // TODO -  so that we can keep things like bulleted lists, font sizes, etc
        public string CreateTomboyNoteContent(Evernote.EDAM.Type.Note note)
        {
            string evernoteContent = _noteStore.getNoteContent(_authToken, note.Guid);
            evernoteContent = Regex.Replace(evernoteContent, @"<(.|\n)*?>", string.Empty); //TODO - this is bad, replace this
            evernoteContent = Regex.Replace(evernoteContent, "<br clear=\"none\"/>", "\n");
            /*string lcontent = content.ToLowerInvariant();
            int begin = lcontent.IndexOf("<en-note>") + 9;
            int end = lcontent.LastIndexOf("</en-note>") - 1;
            if (end-begin < 1)
            {
                Logger.Warn("Null note content in Evernote");
                return "";
            }
            return content.Substring(begin,end-begin);*/

            
            string TomboyHeader = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<note version=\"" + NoteArchiver.CURRENT_VERSION + 
                "\" xmlns:link=\"http://beatniksoftware.com/tomboy/link\" xmlns:size=\"http://beatniksoftware.com/tomboy/size\"" +
            " xmlns=\"http://beatniksoftware.com/tomboy\">";
            string content = TomboyHeader + "\n" + "<title>" + note.Title + "</title>" +
                             "<text xml:space=\"preserve\"><note-content version=\"0.1\">";
            content += evernoteContent;
            content += "</note-content></text>\n";
            DateTime date = new DateTime(note.Updated);
            content += "<last-change-date>" + date.ToString() + "</last-change-date>\n";            
            content += "<last-metadata-change-date>" + date.ToString() + "</last-metadata-change-date>\n";
            date = new DateTime(note.Created);
            content += "<create-date>" + date.ToString() + "</create-date>\n";
            content += "</note>";
            return content;
        }

        public void DeleteNotes(IList<string> deletedNoteUUIDs)
        {
            Logger.Debug("**Not deleting " + deletedNoteUUIDs.Count + "notes***");
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
            Logger.Debug("Doing UploadNotes");
            
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
                    UpdateEvernote(enote, note);
                } 
                else 
                {
                    //does not exist, so create a new note.
                    if (!CreateNewEvernote(note))
                    {
                        Logger.Debug("Problem creating note with id" + note.Id);
                        throw new Exception("Couldn't create note");
                    }
                }
            }
        }

        public bool CreateNewEvernote(Note note) 
        {
            Evernote.EDAM.Type.Note newNote = new Evernote.EDAM.Type.Note();                        
            newNote = FillEvernote(note, newNote);
            try
            {
                _noteStore.createNote(_authToken, newNote);
            }
            catch (Exception e)
            {
                Logger.Error(e.ToString());
                return false;
            }
            return true;
        }
        public Evernote.EDAM.Type.Note FillEvernote(Note tomboynote, Evernote.EDAM.Type.Note evernote)
        {
			Logger.Debug("Creating new Evernote from tomboy:" + tomboynote.Id.ToString());
			if (evernote.Attributes == null) {
				evernote.Attributes = new NoteAttributes();
			}
            evernote.Attributes.SourceApplication = "tomboy"; //this note came from tomboy. This is read later to match guids
            evernote.Attributes.Source = tomboynote.Id;
            string tomboyRawContent = tomboynote.XmlContent;
            //TODO - this needs to be fixed
            string content = Regex.Replace(tomboynote.TextContent, "\n", "<br clear=\"none\"/>\n");
            //TODO - this should be made more general
            evernote.Content =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<!DOCTYPE en-note SYSTEM \"http://xml.evernote.com/pub/enml.dtd\"><en-note>";
            evernote.Content +=  SecurityElement.Escape(content);
            evernote.Content += "</en-note>";

            
            evernote.Title = tomboynote.Title;
            if (tomboynote.Tags.Count>0) {
                evernote.TagNames = new List<string>();
	            foreach (Tag tag in tomboynote.Tags)
	            {
	                evernote.TagNames.Add(tag.Name);
	            }
			}
            evernote.NotebookGuid = _tomboyNotebook.Guid;
            //TODO - check these times, this was just a guess
            evernote.Created = (long)tomboynote.CreateDate.Subtract(Epoch).TotalMilliseconds;
            evernote.Updated = (long)tomboynote.ChangeDate.Subtract(Epoch).TotalMilliseconds;
            return evernote;
        }       

        public bool UpdateEvernote(Evernote.EDAM.Type.Note evernote,  Note tomboynote)
        {
			Logger.Debug("Updating evernote: " + evernote.Guid);
            try
            {
                evernote = FillEvernote(tomboynote, evernote);
                _noteStore.updateNote(_authToken, evernote);
            }
            catch (Exception e)
            {
				Logger.Warn(e.ToString());
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
                
                //return _noteStore.getSyncState(_authToken).UpdateCount;
                return highestRevision;
            }
        }

        //TODO - this obviously needs help
        public SyncLockInfo CurrentSyncLock
        {
            get 
            {                 
                return _syncLock;
            }
        }

        public string Id
        {
            get { return "Evernote-0001"; }
        }
    }
}
