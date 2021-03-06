using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Linq;
using HomeSchoolCore.APIRespond;
using HomeSchoolCore.Models;
using Microsoft.AspNetCore.Http;
using MongoDB.Driver;
using HomeSchoolCore.APIRequest;

namespace HomeSchoolCore.Helpers
{
    public class ApiHelper : IApiHelper
    {
        private IMongoCollection<Class> _classes;
        private IMongoCollection<User> _users;
        private IMongoCollection<Subject> _subjects;
        private IMongoCollection<Homework> _homeworks;
        private IMongoCollection<ResponseToHomework> _responses;
        private IMongoCollection<FileDoc> _files;
        private IMongoCollection<TextMessage> _messages;
        private IMongoCollection<ChatInfo> _chatInfos;
        private IMongoCollection<Quiz> _quizes;
        private IMongoCollection<ResponseToQuiz> _quizesAnswers;
        private IMongoCollection<QuizQuestion> _quizQuestions;
        private IMongoDatabase database;
        public ApiHelper()
        {
            var client = new MongoClient(AppSettingsHelper.connectionString);
            database = client.GetDatabase("ELearningDB");
            _users = database.GetCollection<User>("Users");
            _classes = database.GetCollection<Class>("Classes");
            _subjects = database.GetCollection<Subject>("Subjects");
        }

        #region UsersMethods
        public async Task<User> LoginUser(string email, string password)
        {
            var user = await _users.Find<User>(user => user.email == email).FirstOrDefaultAsync();
            if(user == null) return null;

            if(!VerifyPasswordHash(password, user.PasswordHash, user.PasswordSalt)) return null;

            return user;
        }

        private bool VerifyPasswordHash(string password, byte[] passwordHash, byte[] passwordSalt)
        {
            using (var hmac = new HMACSHA512(passwordSalt))
            {
                var computedHash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
                for (int i = 0; i < computedHash.Length; i++)
                {
                if(computedHash[i] != passwordHash[i]) return false;
                }
            }
            return true;
        }

        public async Task<User> RegisterUser(User user, string password)
        {
            
            byte[] passwordHash, passwordSalt;
            CreatePasswordHash(password, out passwordHash, out passwordSalt);
            user.PasswordHash = passwordHash;
            user.PasswordSalt = passwordSalt;
            await _users.InsertOneAsync(user);
            return user;

        }

        private void CreatePasswordHash(string password, out byte[] passwordHash, out byte[] passwordSalt)
        {
            using (var hmac = new HMACSHA512())
            {
                passwordSalt = hmac.Key;
                passwordHash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
            }
        }

        public async Task<bool> UserExists(string email)
        {
            if(await _users.Find<User>(user => user.email == email).AnyAsync()) return true;
            else return false;
        }
        public UserToReturn ReturnUserToReturn(User user)
        {
            UserToReturn userToReturn = new UserToReturn();
            userToReturn.Id = user.Id;
            userToReturn.Email = user.email;
            userToReturn.Name = user.name;
            userToReturn.Surrname = user.surrname;
            userToReturn.UserRole = user.userRole;
            userToReturn.UserCode = user.userCode;
            return userToReturn;
        }
        public async Task<User> ReturnUserByID(string id)
        {
            var user = await _users.Find<User>(user => user.Id == id).FirstOrDefaultAsync();
            return user;
        }
        public async Task<User> ReturnUserByMail(string email)
        {
            var user = await _users.Find<User>(user => user.email == email).FirstOrDefaultAsync();
            return user;
        }
        public bool DoesUserExistByEmail(string email)
        {
            return _users.Find<User>(user => user.email == email).Any();
        }
        #endregion 

        #region ClasessMethods
        public async Task<ClassToReturn> ReturnClassToReturn(Class classObj, string userID)
        {
            ClassToReturn classToReturn = new ClassToReturn();
            List<SubjectToReturn> subjects = new List<SubjectToReturn>();
            List<SubjectToReturn> subjectsForStudent = new List<SubjectToReturn>();
            classToReturn.Id = classObj.Id;
            classToReturn.CreatorID = classObj.creatorID;
            classToReturn.ClassName = classObj.className;
            classToReturn.SchoolName = classObj.schoolName;
            classToReturn.MembersAmount = classObj.membersAmount;
            classToReturn.Members = classObj.members;
            classToReturn.Subjects = subjects;

            //Dla wychowawcy klasy
            if(classToReturn.CreatorID == userID)
            {
                for (int i = 0; i < classObj.subjects.Count; i++)
                {
                    var subjectObj = await _subjects.Find<Subject>(x => x.Id == classObj.subjects[i]).FirstOrDefaultAsync();
                    var subjectToReturn = await ReturnSubjectToReturn(subjectObj, userID);
                    if(subjectToReturn.TeacherID != userID)
                    {
                        subjectToReturn.Quizes = new List<QuizToReturn>();
                    }


                    classToReturn.Subjects.Add(subjectToReturn);
                }
                return classToReturn;
            }
            else //Dla nauczyciela lub ucznia
            {
                for (int i = 0; i < classObj.subjects.Count; i++)
                {
                    var subjectObj = await _subjects.Find<Subject>(x => x.Id == classObj.subjects[i]).FirstOrDefaultAsync();
                    var subjectToReturn = await ReturnSubjectToReturn(subjectObj, userID);


                    if(subjectObj.teacherID == userID)
                    {
                        classToReturn.Subjects.Add(subjectToReturn);
                    }
                    else
                    {
                        subjectsForStudent.Add(subjectToReturn);
                    }
                }

                if(classToReturn.Subjects.Count == 0)
                {
                    classToReturn.Subjects = subjectsForStudent;
                }

                return classToReturn;
            }
        }
        public async Task<Class> ReturnClassByID(string id)
        {
            try
            {
                var klasa = await _classes.Find<Class>(x => x.Id == id).FirstOrDefaultAsync();
                return klasa;
            }
            catch
            {
                return null;
            }
        }
        public async Task<Class> AddMemberToClass(string email, Class classObj)
        {
            var filter = Builders<Class>.Filter.Eq(x => x.Id, classObj.Id);
            var user = await _users.Find<User>(x => x.email == email).FirstOrDefaultAsync();
            for (int i = 0; i < classObj.members.Count; i++)
            {
                if(classObj.members.Contains(user.Id))
                {
                    return null;
                }
            }
            classObj.members.Add(user.Id);
            classObj.membersAmount++;
            await _classes.ReplaceOneAsync(filter, classObj);
            return classObj;
        }
        public async Task<Class> CreateClass(User creator, string className, string schoolName)
        {
            List<string> members = new List<string>();
            List<string> subjects = new List<string>();

            await database.CreateCollectionAsync(className);

            Class classToAdd = new Class 
            {
                className = className,
                creatorID = creator.Id,
                schoolName = schoolName,
                membersAmount = 0,
                members = members,
                subjects = subjects
            };
            classToAdd.membersAmount++;
            classToAdd.members.Add(creator.Id);
                
            await _classes.InsertOneAsync(classToAdd);       

            await database.RenameCollectionAsync(className, classToAdd.Id);
            await database.CreateCollectionAsync(classToAdd.Id+"_ho");
            await database.CreateCollectionAsync(classToAdd.Id+"_files");
            await database.CreateCollectionAsync(classToAdd.Id+"_quizes");
            await database.CreateCollectionAsync(classToAdd.Id+"_quizesAnswers");
            await database.CreateCollectionAsync(classToAdd.Id+"_quizQuestions");
            var filter = Builders<User>.Filter.Eq(u => u.Id, creator.Id);
            creator.classMember.Add(classToAdd.Id);
            await _users.ReplaceOneAsync(filter, creator);
                    
            return classToAdd;
        }
        public async Task<Class> ReplaceClassInfo(Class classToChange)
        {
            var filter = Builders<Class>.Filter.Eq(x => x.Id, classToChange.Id);
            await _classes.ReplaceOneAsync(filter, classToChange);
            return classToChange;
        }
        public async Task<Class> DeleteMemberFromClass(User user, Class classObj)
        {
            classObj.members.Remove(user.Id);
            await ReplaceClassInfo(classObj);
            user.classMember.Remove(classObj.Id);
            var filter = Builders<User>.Filter.Eq(x => x.Id, user.Id);
            await _users.ReplaceOneAsync(filter, user);
            return classObj;
        }
        public async Task<List<string>> ReturnNames(Class classObj)
        {
            List<string> usersNames = new List<string>();
            for (int i = 0; i < classObj.members.Count; i++)
            {
                var user = await ReturnUserByID(classObj.members[i]);
                usersNames.Add(user.name + " " + user.surrname);
            }
            return usersNames;
        }    
        public async Task<bool> DoesUserBelongToClass(string userId, string classId)
        {

            var classDoc = await _classes.Find<Class>(x => x.Id == classId).FirstOrDefaultAsync();
            foreach(var usrId in classDoc.members)
            {
                if(usrId == userId) return true;
            }
            return false;
        }
        
        #endregion

        #region SubjectsMethods
        public async Task<SubjectReturn> AddSubjectToClass(string teacherID, Class classToEdit, string subjectName)
        {
            Subject subject = new Subject()
            {
                name = subjectName,
                teacherID = teacherID,
                classID = classToEdit.Id,
                homeworks = new List<string>()
            };
            
            await _subjects.InsertOneAsync(subject);
            await database.CreateCollectionAsync(subject.Id+"_chat");
            await database.CreateCollectionAsync(subject.Id+"_chat_info");
            _chatInfos = database.GetCollection<ChatInfo>(subject.Id+"_chat_info");
            ChatInfo chatInfo = new ChatInfo
            {
                classID = classToEdit.Id,
                subjectID = subject.Id,
                messagesNumber = 0
            };
            await _chatInfos.InsertOneAsync(chatInfo);
            classToEdit.subjects.Add(subject.Id);
            
            var isTeacherAlreadyInClass = false;
            for (int i = 0; i < classToEdit.members.Count; i++)
            {
                if(classToEdit.members.Contains(teacherID))
                {
                    isTeacherAlreadyInClass = true;
                }
            }

            var user = await _users.Find<User>(x => x.Id == teacherID).FirstOrDefaultAsync();


            if(!isTeacherAlreadyInClass)
            {
                classToEdit.membersAmount++;
                classToEdit.members.Add(teacherID);
                await ReplaceClassInfo(classToEdit);
                user.classMember.Add(classToEdit.Id);
                var filter = Builders<User>.Filter.Eq(x => x.Id, teacherID);
                await _users.ReplaceOneAsync(filter, user);
            }
        
            SubjectReturn subjectReturn = new SubjectReturn();
            subjectReturn.ClassObj = classToEdit;
            subjectReturn.Subject = subject;
            return subjectReturn; 

        }
        public async Task<Subject> ReturnSubjectBySubjectID(string classID, string subjectID)
        {
            var subject = await _subjects.Find<Subject>(x => x.Id == subjectID).FirstOrDefaultAsync();
            return subject;
        }
        public async Task<SubjectToReturn> ReturnSubjectToReturn(Subject subject, string userID)
        {
            _homeworks = database.GetCollection<Homework>(subject.classID+"_ho");
            SubjectToReturn subjectToReturn = new SubjectToReturn {
                Id = subject.Id,
                Name = subject.name,
                ClassID = subject.classID,
                TeacherID = subject.teacherID,
                Homeworks = new List<HomeworkToReturn>()
            };

            var userClass = await _classes.Find<Class>(x => x.Id == subjectToReturn.ClassID).FirstOrDefaultAsync();
            if(userClass.creatorID == userID && subject.teacherID != userID)
            {
                return subjectToReturn;
            }
            for (int i = 0; i < subject.homeworks.Count; i++)
            {
                var homeworkObj = await _homeworks.Find<Homework>(x => x.Id == subject.homeworks[i]).FirstOrDefaultAsync();
                var homeworkToReturn = await ReturnHomeworkToReturn(homeworkObj, subject.classID, userID);
                subjectToReturn.Homeworks.Add(homeworkToReturn);
            }

            List<QuizToReturn> quizes = await ReturnQuizesForSubject(subject.classID, subject.Id, userID);
            subjectToReturn.Quizes = quizes;
            return subjectToReturn;
        }
        public async Task<bool> IsSubjectDeleted(string classID, string subjectID, string userID)
        {
            _subjects = database.GetCollection<Subject>(classID+"_su");

            var classObj = await _classes.Find<Class>(x => x.Id == classID).FirstOrDefaultAsync();
            var subject = await _subjects.Find<Subject>(x => x.Id == subjectID).FirstOrDefaultAsync();

            if(classObj.subjects.Contains(subjectID))
            {
                if(subject == null) return false;
                for (int i = 0; i < subject.homeworks.Count; i++)
                {
                    await database.DropCollectionAsync(subject.homeworks[i]+"_re");
                    await database.DropCollectionAsync(subject.homeworks[i]+"_re_files");
                }
                _files = database.GetCollection<FileDoc>(classID+"_files");
                var files = await _files.Find<FileDoc>(x => x.subjectID == subjectID).ToListAsync();

                for (int i = 0; i < files.Count; i++)
                {
                    await _files.DeleteOneAsync<FileDoc>(x => x.Id == files[i].Id);
                }

                classObj.subjects.Remove(subjectID);
                var filter = Builders<Class>.Filter.Eq(x => x.Id, classObj.Id);
                await _classes.ReplaceOneAsync(filter, classObj);
                _subjects.DeleteOne<Subject>(x => x.Id == subjectID);
            }

            var subjectObj = await _subjects.Find<Subject>(x => x.teacherID == subject.teacherID).FirstOrDefaultAsync();
            if(subjectObj == null && classObj.creatorID != subject.teacherID)
            {
                classObj.members.Remove(userID);
                await ReplaceClassInfo(classObj);
                var user = await _users.Find<User>(x => x.Id == subject.teacherID).FirstOrDefaultAsync();
                user.classMember.Remove(classID);
                var filter = Builders<User>.Filter.Eq(x => x.Id, user.Id);
                await _users.ReplaceOneAsync(filter, user);
            }
            return true;
        }
        
        #endregion
    
        #region TextMessages
        public async Task<TextMessage> SendMessage(string subjectID, TextMessage textMessage)
        {
            _messages = database.GetCollection<TextMessage>(subjectID+"_chat");
            _chatInfos = database.GetCollection<ChatInfo>(subjectID+"_chat_info");

            var chatInfo = await _chatInfos.Find<ChatInfo>(x => x.subjectID == subjectID).FirstOrDefaultAsync();

            var filter = Builders<ChatInfo>.Filter.Eq(x => x.subjectID, subjectID);

            chatInfo.messagesNumber++;
            textMessage.messageID = chatInfo.messagesNumber;

            await _chatInfos.ReplaceOneAsync(filter, chatInfo);

            await _messages.InsertOneAsync(textMessage);
            return textMessage;
        }
        public async Task<List<TextMessage>> ReturnLastMessages(string subjectID)
        {
            _messages = database.GetCollection<TextMessage>(subjectID+"_chat");
            _chatInfos = database.GetCollection<ChatInfo>(subjectID+"_chat_info");
            var chatInfo = await _chatInfos.Find<ChatInfo>(x => x.subjectID == subjectID).FirstOrDefaultAsync();
            int lastMessageID = chatInfo.messagesNumber;
            List<TextMessage> messages = new List<TextMessage>();
            for (int i = 0; i < 10; i++)
            {
                var textMessage = await _messages.Find<TextMessage>(x => x.messageID == lastMessageID).FirstOrDefaultAsync();
                if(textMessage == null) return messages;
                messages.Add(textMessage);
                lastMessageID--;
            }
            return messages;
        }
        public async Task<List<TextMessage>> ReturnNewerMessages(int lastMessageID, string subjectID)
        {
            _messages = database.GetCollection<TextMessage>(subjectID+"_chat");
            _chatInfos = database.GetCollection<ChatInfo>(subjectID+"_chat_info");
            var chatInfo = await _chatInfos.Find<ChatInfo>(x => x.subjectID == subjectID).FirstOrDefaultAsync();
            var messagesNumer = chatInfo.messagesNumber;
            var sum = messagesNumer - lastMessageID;
            if(sum <= 0) return null;
            lastMessageID++;
            List<TextMessage> messages = new List<TextMessage>();
            for (int i = 0; i < sum; i++)
            {
                var textMessage = await _messages.Find<TextMessage>(x => x.messageID == lastMessageID).FirstOrDefaultAsync();
                if(textMessage == null) return messages;
                messages.Add(textMessage);
                lastMessageID++;
            }
            return messages;
        }
        public async Task<List<TextMessage>> ReturnOlderMessages(int lastMessageID, string subjectID)
        {
            _messages = database.GetCollection<TextMessage>(subjectID+"_chat");
            _chatInfos = database.GetCollection<ChatInfo>(subjectID+"_chat_info");
            lastMessageID--;
            List<TextMessage> messages = new List<TextMessage>();
            for (int i = 0; i < 10; i++)
            {
                var textMessage = await _messages.Find<TextMessage>(x => x.messageID == lastMessageID).FirstOrDefaultAsync();
                if(textMessage == null) return messages;
                messages.Add(textMessage);
                lastMessageID--;
            }
            return messages;
        }
        #endregion
    
        #region HomeworkMethods
        public async Task<Homework> AddHomeworkToSubject(Subject subject, string name, string description, DateTime time, List<string> filesID, List<string> linkHrefs)
        {
            _homeworks = database.GetCollection<Homework>(subject.classID+"_ho");
            _files = database.GetCollection<FileDoc>(subject.classID+"_files");
            for (int i = 0; i < filesID.Count; i++)
            {
                var fileObj = await _files.Find<FileDoc>(x => x.Id == filesID[i]).FirstOrDefaultAsync();
                if(fileObj == null) return null;
            }
            var homework = new Homework()
            {
                name = name,
                description = description,
                subjectID = subject.Id,
                createDate = DateTime.Now,
                teacherID = subject.teacherID,
                files = filesID,
                endDate = time,
                responses = new List<string>(),
                linkHrefs = linkHrefs
            };
            await _homeworks.InsertOneAsync(homework);
            await database.CreateCollectionAsync(homework.Id+"_re");
            await database.CreateCollectionAsync(homework.Id+"_re_files");
            subject.homeworks.Add(homework.Id);
            var filter = Builders<Subject>.Filter.Eq(x => x.Id, subject.Id);
            await _subjects.ReplaceOneAsync(filter, subject);
            return homework;
        }
        public async Task<ResponseReturn> CreateResponse(ResponseToHomework response, string classID, Homework homework)
        {
            try
            {
                _responses = database.GetCollection<ResponseToHomework>(response.homeworkID+"_re");
                _homeworks = database.GetCollection<Homework>(classID+"_ho");
            }
            catch
            {
                return null;
            }
            await _responses.InsertOneAsync(response);
            
            if(DateTime.Compare(DateTime.Now, homework.endDate) > 0)
            {
                return null;
            }

            homework.responses.Add(response.Id);
            var filter = Builders<Homework>.Filter.Eq(x => x.Id, homework.Id);
            await _homeworks.ReplaceOneAsync(filter, homework);
            ResponseReturn responseReturn = new ResponseReturn
            {
                HomeworkObj = homework,
                ResponseObj = response,
                HomeworkName = homework.name
            };
            return responseReturn; 
        }
        public async Task<HomeworkToReturn> ReturnHomeworkToReturn(Homework homework, string classID, string userID)
        {
            HomeworkToReturn homeworkToReturn = new HomeworkToReturn {
                Id = homework.Id,
                Name = homework.name,
                Description = homework.description,
                SubjectID = homework.subjectID,
                Responses = new List<ResponseToHomework>(),
                CreateDate = homework.createDate,
                EndDate = homework.endDate,
                Files = homework.files.ToArray(),
                LinkHrefs = homework.linkHrefs.ToArray()
            };
            
            List<ResponseToHomework> userResponses = new List<ResponseToHomework>();
            _responses = database.GetCollection<ResponseToHomework>(homework.Id+"_re");
            var subject = await _subjects.Find<Subject>(x => x.Id == homework.subjectID).FirstOrDefaultAsync();
            for (int i = 0; i < homework.responses.Count; i++)
            {
                var response = await _responses.Find<ResponseToHomework>(x => x.Id == homework.responses[i]).FirstOrDefaultAsync();
                if(response.senderID == userID) userResponses.Add(response);
                homeworkToReturn.Responses.Add(response);
            }
            if(subject.teacherID == userID)
            {
                return homeworkToReturn;
            }
            else
            {
                homeworkToReturn.Responses = userResponses;
                return homeworkToReturn;            
            }
        }
        public async Task<ResponseToHomework> PutMark(string homeworkID, string responseID, string mark)
        {
            _responses = database.GetCollection<ResponseToHomework>(homeworkID+"_re");
            var response = await _responses.Find<ResponseToHomework>(x => x.Id == responseID).FirstOrDefaultAsync();
            response.mark = mark;
            var filter = Builders<ResponseToHomework>.Filter.Eq(x => x.Id, responseID);
            await _responses.ReplaceOneAsync(filter, response);
            return response;
        }
        public async Task<string> UploadFileToHomework(IFormFile file, string classID, string senderID, string subjectID)
        {

            _files = database.GetCollection<FileDoc>(classID+"_files");
            byte[] binaryContent;
            using(var uploadedFile = file.OpenReadStream())
            {
                using(var memoryStream = new MemoryStream())
                {
                    uploadedFile.CopyTo(memoryStream);
                    binaryContent = memoryStream.ToArray();
                } 
            }
            FileDoc fileDoc = new FileDoc
            {
                fileContent = binaryContent,
                senderID = senderID,
                contentType = file.ContentType,
                fileName = file.FileName,
                subjectID = subjectID,
                classID = classID
            };

            await _files.InsertOneAsync(fileDoc);
            return fileDoc.Id;
        }
        public async Task<Homework> ReturnHomeworkByIDs(string classID, string homeworkID)
        {
            _homeworks = database.GetCollection<Homework>(classID+"_ho");
            var homework = await _homeworks.Find<Homework>(x => x.Id == homeworkID).FirstOrDefaultAsync();
            return homework;
        }       
        public async Task<ReturnFile> ReturnHomeworkFileBySenderID(string classID, string fileID)
        {
            try
            {
                _files = database.GetCollection<FileDoc>(classID+"_files");
            }
            catch (System.Exception)
            {
                return null;
            }
            var fileObj = await _files.Find<FileDoc>(x => x.Id == fileID).FirstOrDefaultAsync();
            var stream = new MemoryStream(fileObj.fileContent);
            ReturnFile returnFile = new ReturnFile();
            returnFile.FileName = fileObj.fileName;
            returnFile.ContentType = fileObj.contentType;
            returnFile.Stream = stream;
            returnFile.SenderID = fileObj.senderID;
            returnFile.SubjectID = fileObj.subjectID;
            returnFile.ClassID = fileObj.classID;
            return returnFile;
        }
        public async Task<string> UploadFileToResponse(IFormFile file, string homeworkID, string senderID, string subjectID, string classID)
        {
            try
            {
                _files = database.GetCollection<FileDoc>(homeworkID+"_re_files");
            }
            catch
            {
                return null;
            }
            byte[] binaryContent;
            using(var uploadedFile = file.OpenReadStream())
            {
                using(var memoryStream = new MemoryStream())
                {
                    uploadedFile.CopyTo(memoryStream);
                    binaryContent = memoryStream.ToArray();
                } 
            }
            FileDoc fileDoc = new FileDoc
            {
                fileContent = binaryContent,
                senderID = senderID,
                contentType = file.ContentType,
                fileName = file.FileName,
                subjectID = subjectID,
                classID = classID
            };

            await _files.InsertOneAsync(fileDoc);
            return fileDoc.Id;
        }
        public async Task<ReturnFile> ReturnResponseFileBySenderID(string homeworkID, string fileID)
        {
            try
            {
                _files = database.GetCollection<FileDoc>(homeworkID+"_re_files");
            }
            catch
            {
                return null;
            }
            var fileObj = await _files.Find<FileDoc>(x => x.Id == fileID).FirstOrDefaultAsync();
            var stream = new MemoryStream(fileObj.fileContent);
            ReturnFile returnFile = new ReturnFile();
            returnFile.FileName = fileObj.fileName;
            returnFile.ContentType = fileObj.contentType;
            returnFile.Stream = stream;
            returnFile.SenderID = fileObj.senderID;
            returnFile.SubjectID = fileObj.subjectID;
            returnFile.ClassID = fileObj.classID;
            return returnFile;
        }
        public async Task<bool> IsHomeworkDeleted(string homeworkID, string subjectID, string classID)
        {
            try
            {
                _homeworks = database.GetCollection<Homework>(classID+"_ho");
                _files = database.GetCollection<FileDoc>(classID+"_files");
            }
            catch 
            {
                return false;
            }
            var homework = await _homeworks.Find<Homework>(x => x.Id == homeworkID).FirstOrDefaultAsync();
            if(homework.subjectID != subjectID)
            {
                return false;
            }
            var filter = Builders<Subject>.Filter.Eq(x => x.Id, subjectID);
            var subject = await _subjects.Find<Subject>(x => x.Id == subjectID).FirstOrDefaultAsync();
            subject.homeworks.Remove(homework.Id);
            await _subjects.ReplaceOneAsync(filter, subject);
            for (int i = 0; i < homework.files.Count; i++)
            {
                await _files.DeleteOneAsync(x => x.Id == homework.files[i]);
            }
            await _homeworks.DeleteOneAsync<Homework>(x => x.Id == homeworkID);
            return true;
        }
        #endregion
    
        #region Quizes
        public async Task<Quiz> IsQuizAdded(CreateQuizDTO createQuiz)
        {
            _quizes = database.GetCollection<Quiz>(createQuiz.classID+"_quizes");
            _quizQuestions = database.GetCollection<QuizQuestion>(createQuiz.classID+"_quizQuestions");
            Quiz quiz = new Quiz();
            QuizQuestion questionsToAdd = new QuizQuestion();
            quiz.name = createQuiz.name;
            quiz.classID = createQuiz.classID;
            quiz.subjectID = createQuiz.subjectID;
            quiz.CreateDate = DateTime.Now;
            quiz.StartDate = createQuiz.StartDate;
            quiz.FinishDate = createQuiz.FinishDate;
            quiz.amountOfQuestions = createQuiz.questions.Count;
            quiz.status = "ACTIVE";

            await _quizes.InsertOneAsync(quiz);

            questionsToAdd.QuizId = quiz.Id;
            questionsToAdd.Questions.AddRange(createQuiz.questions);

            await _quizQuestions.InsertOneAsync(questionsToAdd);


            return quiz;
        }


        public async Task<List<QuizToReturn>> ReturnQuizesForSubject(string classId, string subjectId, string userId)
        {
            _quizes = database.GetCollection<Quiz>(classId+"_quizes");
            _quizesAnswers = database.GetCollection<ResponseToQuiz>(classId + "_quizesAnswers");
            var user = await _users.Find<User>(x => x.Id == userId).FirstOrDefaultAsync();

            List<Quiz> quizes;

            if(user.userRole == 0)
            {
                quizes = await _quizes.Find<Quiz>(x => x.status == "ACTIVE" && x.subjectID == subjectId).ToListAsync();

                List<QuizToReturn> quizesToReturn = new List<QuizToReturn>();
                List<Quiz> quizesToExcept = new List<Quiz>();
                foreach (var quiz in quizes)
                {
                    QuizToReturn quizToReturn = new QuizToReturn {
                        amountOfQuestions = quiz.amountOfQuestions,
                        classID = quiz.classID,
                        FinishDate = quiz.FinishDate,
                        StartDate = quiz.StartDate,
                        Id = quiz.Id,
                        name = quiz.name,
                        status = quiz.status,
                        subjectID = quiz.subjectID
                    };

                    if(DateTime.Compare(quiz.FinishDate, DateTime.Now) < 0)
                    {
                        quiz.status = "INACTIVE";
                        await _quizes.FindOneAndReplaceAsync(x => x.Id == quiz.Id, quiz);
                        quizesToExcept.Add(quiz);
                    }
                    else
                    {
                        if(quiz.executonersId.Contains(user.Id)) quizToReturn.status = "INACTIVE";
                        quizesToReturn.Add(quizToReturn);
                    }
                }

                if(quizesToExcept.Count != 0)
                {
                    quizes = quizes.Except(quizesToExcept).ToList();
                }

                return quizesToReturn;
            }
            else
            {
                var subject = await _subjects.Find<Subject>(x => x.teacherID == userId && x.classID == classId).FirstOrDefaultAsync();
                if(subject == null) return null;

                quizes = await _quizes.Find<Quiz>(x => x.subjectID == subject.Id).ToListAsync();

                List<QuizToReturn> quizesToReturn = new List<QuizToReturn>();

                foreach (var quiz in quizes)
                {
                    if(DateTime.Compare(quiz.FinishDate, DateTime.Now) < 0)
                    {
                        quiz.status = "INACTIVE";
                        await _quizes.FindOneAndReplaceAsync(x => x.Id == quiz.Id, quiz);
                    }

                    QuizToReturn quizToReturn = new QuizToReturn {
                        amountOfQuestions = quiz.amountOfQuestions,
                        classID = quiz.classID,
                        FinishDate = quiz.FinishDate,
                        StartDate = quiz.StartDate,
                        Id = quiz.Id,
                        name = quiz.name,
                        status = quiz.status,
                        subjectID = quiz.subjectID
                    };
                    
                    quizesToReturn.Add(quizToReturn);
                }

                return quizesToReturn;
            }
        }

        public async Task<List<AnswerToReturn>> GetAnswersForStudent(string id)
        {
            var user = await _users.Find(x => x.Id == id).FirstOrDefaultAsync();
            if(user.userRole != 0) return null;

            _quizesAnswers = database.GetCollection<ResponseToQuiz>(user.classMember.First() + "_quizesAnswers");
            var answers = await _quizesAnswers.Find(x => x.executonerId == id).ToListAsync();

            List<AnswerToReturn> answersToReturn = new List<AnswerToReturn>();
            foreach (var answer in answers)
            {
                answersToReturn.Add(new AnswerToReturn {
                    Executoner = $"{user.name} {user.surrname}",
                    PercentageOfCorrectAnswers = answer.percentageOfCorrectAnswers,
                    QuizId = answer.quizId
                });
            }

            return answersToReturn;
        }

        public async Task<List<QuestionToReturn>> ReturnQuestionsForQuiz(string classId, string quizId)
        {
            _quizQuestions = database.GetCollection<QuizQuestion>(classId + "_quizQuestions");
            var questions = await _quizQuestions.Find<QuizQuestion>(x => x.QuizId == quizId).FirstOrDefaultAsync();
            if(questions == null) return null;

            List<QuestionToReturn> questionsToReturn = new List<QuestionToReturn>();
            foreach (var question in questions.Questions)
            {
                var questionToAdd = new QuestionToReturn();
                questionToAdd.question = question.question;
                questionToAdd.Answers.Add(question.correctAnswer);
                questionToAdd.Answers.Add(question.incorrectAnswer1);
                questionToAdd.Answers.Add(question.incorrectAnswer2);
                questionToAdd.Answers.Add(question.incorrectAnswer3);
                Random rng = new Random();
                int n = questionToAdd.Answers.Count;  
                while (n > 1) {  
                    n--;  
                    int k = rng.Next(n + 1);  
                    string value = questionToAdd.Answers[k];  
                    questionToAdd.Answers[k] = questionToAdd.Answers[n];  
                    questionToAdd.Answers[n] = value;  
                }  
                questionsToReturn.Add(questionToAdd);
            }
            return questionsToReturn;
        }
        
        public async Task<Quiz> ReturnQuizById(string classId, string quizId)
        {
            try
            {
                _quizes = database.GetCollection<Quiz>(classId+"_quizes");
                var quiz = await _quizes.Find<Quiz>(x => x.Id == quizId).FirstOrDefaultAsync();
                return quiz;
            }
            catch
            {
                return null;
            }
        }
        
        public async Task<bool> SaveAnswersToQuiz(ResponseToQuiz responseToQuiz)
        {
            _quizesAnswers = database.GetCollection<ResponseToQuiz>(responseToQuiz.classId + "_quizesAnswers");
            _quizes = database.GetCollection<Quiz>(responseToQuiz.classId+"_quizes");

            var quiz = await _quizes.Find<Quiz>(x => x.Id == responseToQuiz.quizId).FirstOrDefaultAsync();
            if(quiz.executonersId.Contains(responseToQuiz.executonerId)) return false; 

            await _quizesAnswers.InsertOneAsync(responseToQuiz);

            quiz.executonersId.Add(responseToQuiz.executonerId);

            await _quizes.FindOneAndReplaceAsync(x => x.Id == quiz.Id, quiz);

            return true;
        }
        
        public async Task<QuizQuestion> ReutrnCorrectQuizQuestions(string classId, string quizId)
        {
            _quizQuestions = database.GetCollection<QuizQuestion>(classId + "_quizQuestions");
            var questions = await _quizQuestions.Find<QuizQuestion>(x => x.QuizId == quizId).FirstOrDefaultAsync();
            if(questions == null) return null;

            return questions;
        }


        public async Task<List<AnswerToReturn>> GetAnswersForQuiz(string userId, string classId, string subjectId, string quizId)
        {
            var user = await _users.Find(x => x.Id == userId).FirstOrDefaultAsync();
            if(user.userRole == 0)
            {
                _quizesAnswers = database.GetCollection<ResponseToQuiz>(classId + "_quizesAnswers");
                var answer = await _quizesAnswers.Find(x => x.executonerId == userId && x.quizId == quizId).FirstOrDefaultAsync();
                if (answer == null) return null;

                List<AnswerToReturn> answersToReturn = new List<AnswerToReturn>();
                answersToReturn.Add(new AnswerToReturn {
                    Executoner = $"{user.name} {user.surrname}",
                    PercentageOfCorrectAnswers = answer.percentageOfCorrectAnswers,
                    QuizId = quizId
                });

                return answersToReturn;
            }
            else
            {
                _quizesAnswers = database.GetCollection<ResponseToQuiz>(classId + "_quizesAnswers");

                var subject = await _subjects.Find(x => x.Id == subjectId).FirstOrDefaultAsync();
                if(subject.teacherID == userId)
                {
                    var answers = await _quizesAnswers.Find(x => x.quizId == quizId).ToListAsync();
                    List<AnswerToReturn> answersToReturn = new List<AnswerToReturn>();
                    foreach (var answer in answers)
                    {
                        var userFromAnswer = await _users.Find(x => x.Id == answer.executonerId).FirstOrDefaultAsync();
                        answersToReturn.Add(new AnswerToReturn {
                            Executoner = $"{userFromAnswer.name} {userFromAnswer.surrname}",
                            PercentageOfCorrectAnswers = answer.percentageOfCorrectAnswers,
                            QuizId = answer.quizId
                        });
                    }
                    return answersToReturn;
                } 
                else return null;

            }


        }
        #endregion
    }
}