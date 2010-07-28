<?xml version="1.0" encoding="UTF-8" ?>
<!DOCTYPE html
PUBLIC "-//W3C//DTD XHTML 1.0 Strict//EN"
"http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd">
<html xmlns="http://www.w3.org/1999/xhtml" xml:lang="en" lang="en">
    <head>
        <meta http-equiv="Content-Type" content="text/html; charset=UTF-8" />
        <title>Glassfish Chat - Updated by Ido Ran</title>
        <link rel="stylesheet" href="stylesheets/default.css" type="text/css" />
        <script type="text/javascript" src="javascripts/prototype.js"></script>
        <script type="text/javascript" src="javascripts/behaviour.js"></script>
        <script type="text/javascript" src="javascripts/moo.fx.js"></script>
        <script type="text/javascript" src="javascripts/moo.fx.pack.js"></script>
        <script type="text/javascript" src="javascripts/application.js"></script>
    </head>
    <body>
        <div id="container">
            <div id="container-inner">
                <div id="header">
                    <h1>Glassfish Chat</h1>
                </div>
                <div id="main">
                    <div id="display">
                    </div>
                    <div id="form">
                        <div id="system-message">Please input your name:</div>
                        <div id="login-form">
                            <input id="login-name" type="text" />
                            <br />
                            <input id="login-button" type="button" value="Login" />
                            <br/>
                            <div id="missing-sockets">Your browser does not support websockets.</div>
                        </div>
                        <div id="message-form" style="display: none;">
                            <div>
                                <textarea id="message" name="message" rows="2" cols="40"></textarea>
                                <br />
                                <input id="post-button" type="button" value="Post Message" />
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
        <iframe id="websockets-frame" style="display: none;"></iframe>
    </body>
</html>