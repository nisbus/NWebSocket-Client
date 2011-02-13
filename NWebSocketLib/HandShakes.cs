using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

/// <summary>
/// This class was originally downloaded from http://nugget.codeplex.com/
/// 
/// Copyright (c) 2010 
///Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
///The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
/// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
/// </summary>
namespace NWebSocketLib
{
    public class ClientHandshake
    {
        public string Origin { get; set; }
        public string Host { get; set; }
        public string ResourcePath { get; set; }
        public string Key1 { get; set; }
        public string Key2 { get; set; }
        public ArraySegment<byte> ChallengeBytes { get; set; }
        public HttpCookieCollection Cookies { get; set; }
        public string SubProtocol { get; set; }
        public Dictionary<string, string> AdditionalFields { get; set; }

        /// <summary>
        /// Put together a string with the values of the handshake - excluding the challenge
        /// </summary>
        public override string ToString()
        {
            var stringShake = "GET " + ResourcePath + " HTTP/1.1\r\n" +
                              "Upgrade: WebSocket\r\n" +
                              "Connection: Upgrade\r\n" +
                              "Origin: " + Origin + "\r\n" +
                              "Host: " + Host + "\r\n" +
                              "Sec-Websocket-Key1: " + Key1 + "\r\n" +
                              "Sec-Websocket-Key2: " + Key2 + "\r\n";


            if (Cookies != null)
            {
                stringShake += "Cookie: " + Cookies.ToString() + "\r\n";
            }
            if (SubProtocol != null)
                stringShake += "Sec-Websocket-Protocol: " + SubProtocol + "\r\n";

            if (AdditionalFields != null)
            {
                foreach (var field in AdditionalFields)
                {
                    stringShake += field.Key + ": " + field.Value + "\r\n";
                }
            }
            stringShake += "\r\n";
            stringShake += Encoding.UTF8.GetString(ChallengeBytes.Array);
            return stringShake;
        }
    }

    public class ServerHandshake
    {
        public string Origin { get; set; }
        public string Location { get; set; }
        public byte[] AnswerBytes { get; set; }
        public string SubProtocol { get; set; }
        public Dictionary<string, string> AdditionalFields { get; set; }
    }

    public class HandshakeHelper
    {
        public static ClientHandshake ParseClientHandshake(ArraySegment<byte> byteShake)
        {
            // the "grammar" of the handshake
            var pattern = @"^(?<connect>[^\s]+)\s(?<path>[^\s]+)\sHTTP\/1\.1\r\n" +  // request line
                          @"((?<field_name>[^:\r\n]+):\s(?<field_value>[^\r\n]+)\r\n)+"; // unordered set of fields (name-chars colon space any-chars cr lf)

            // subtract the challenge bytes from the handshake
            var handshake = new ClientHandshake();
            ArraySegment<byte> challenge = new ArraySegment<byte>(byteShake.Array, byteShake.Count - 8, 8); // -8 : eight byte challenge
            handshake.ChallengeBytes = challenge;

            // get the rest of the handshake
            var utf8_handshake = Encoding.UTF8.GetString(byteShake.Array, 0, byteShake.Count - 8);

            // match the handshake against the "grammar"
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            var match = regex.Match(utf8_handshake);
            var fields = match.Groups;

            // save the request path
            handshake.ResourcePath = fields["path"].Value;

            // run through every match and save them in the handshake object
            for (int i = 0; i < fields["field_name"].Captures.Count; i++)
            {
                var name = fields["field_name"].Captures[i].ToString();
                var value = fields["field_value"].Captures[i].ToString();

                switch (name.ToLower())
                {
                    case "sec-websocket-key1":
                        handshake.Key1 = value;
                        break;
                    case "sec-websocket-key2":
                        handshake.Key2 = value;
                        break;
                    case "sec-websocket-protocol":
                        handshake.SubProtocol = value;
                        break;
                    case "origin":
                        handshake.Origin = value;
                        break;
                    case "host":
                        handshake.Host = value;
                        break;
                    case "cookie":
                        // create and fill a cookie collection from the data in the handshake
                        handshake.Cookies = new HttpCookieCollection();
                        var cookies = value.Split(';');
                        foreach (var item in cookies)
                        {
                            // the name if before the '=' char
                            var c_name = item.Remove(item.IndexOf('='));
                            // the value is after
                            var c_value = item.Substring(item.IndexOf('=') + 1);
                            // put the cookie in the collection (this also parses the sub-values and such)
                            handshake.Cookies.Add(new HttpCookie(c_name.TrimStart(), c_value));
                        }
                        break;
                    default:
                        // some field that we don't know about
                        if (handshake.AdditionalFields == null)
                            handshake.AdditionalFields = new Dictionary<string, string>();
                        handshake.AdditionalFields[name] = value;
                        break;
                }
            }
            return handshake;
        }

        public static byte[] GenerateResponseHandshake(ClientHandshake clientHandshake)
        {
            var responseHandshake = new ServerHandshake();
            responseHandshake.Location = "ws://" + clientHandshake.Host + clientHandshake.ResourcePath;
            responseHandshake.Origin = clientHandshake.Origin;
            responseHandshake.SubProtocol = clientHandshake.SubProtocol;

            var challenge = new byte[8];
            Array.Copy(clientHandshake.ChallengeBytes.Array, clientHandshake.ChallengeBytes.Offset, challenge, 0, 8);
            
            responseHandshake.AnswerBytes = CalculateAnswerBytes(clientHandshake.Key1, clientHandshake.Key2, clientHandshake.ChallengeBytes); 

            return CreateServerHandshake(responseHandshake);
        }

        private static byte[] CreateServerHandshake(ServerHandshake handshake)
        {
            var stringShake = "HTTP/1.1 101 WebSocket Protocol Handshake\r\n" +
                              "Upgrade: WebSocket\r\n" +
                              "Connection: Upgrade\r\n" +
                              "Sec-WebSocket-Origin: " + handshake.Origin + "\r\n" +
                              "Sec-WebSocket-Location: " + handshake.Location + "\r\n";

            if (handshake.SubProtocol != null)
            {
                stringShake += "Sec-WebSocket-Protocol: " + handshake.SubProtocol + "\r\n";
            }
            stringShake += "\r\n";

            // generate a byte array representation of the handshake including the answer to the challenge
            byte[] byteResponse = Encoding.ASCII.GetBytes(stringShake);
            int byteResponseLength = byteResponse.Length;
            Array.Resize(ref byteResponse, byteResponseLength + handshake.AnswerBytes.Length);
            Array.Copy(handshake.AnswerBytes, 0, byteResponse, byteResponseLength, handshake.AnswerBytes.Length);
            return byteResponse;
        }
        
        public static byte[] CalculateAnswerBytes(string key1, string key2, ArraySegment<byte> challenge)
        {
            // the following code is to conform with the protocol

            //  count the spaces
            int spaces1 = key1.Count(x => x == ' ');
            int spaces2 = key2.Count(x => x == ' ');

            // concat the digits
            var digits1 = new String(key1.Where(x => Char.IsDigit(x)).ToArray());            
            var digits2 = new String(key2.Where(x => Char.IsDigit(x)).ToArray());

            // divide the digits with the number of spaces
            Int32 result1 = (Int32)(Int64.Parse(digits1) / spaces1);
            Int32 result2 = (Int32)(Int64.Parse(digits2) / spaces2);

            // convert the results to 32 bit big endian byte arrays
            byte[] result1bytes = BitConverter.GetBytes(result1);
            byte[] result2bytes = BitConverter.GetBytes(result2);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(result1bytes);
                Array.Reverse(result2bytes);
            }

            // concat the two integers and the 8 challenge bytes from the client
            byte[] rawAnswer = new byte[16];
            Array.Copy(result1bytes, 0, rawAnswer, 0, 4);
            Array.Copy(result2bytes, 0, rawAnswer, 4, 4);
            Array.Copy(challenge.Array, challenge.Offset, rawAnswer, 8, 8);

            // compute the md5 hash
            MD5 md5 = System.Security.Cryptography.MD5.Create();
            return md5.ComputeHash(rawAnswer);
        }   
    }
}
