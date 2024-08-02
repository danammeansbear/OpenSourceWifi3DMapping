# OpenSourceWifi3DMapping
This project is a 3D scanner for WiFi signals that can be used to map structures within an enviroment.

I built this thing to visualize how WiFi moves through space. To do so I used an app created in unity for multiplatform support to generate log data. The phone grid pattern and every few feet along an wall infront and in back of it a measurement was captured.

log data was sent over with rabbitmq and ELK using log4net and amqp. 
The Idea is we can ping mutiple devices on the network and on a raycast type situation map the signal strength at that point.
Going forward we will just call them raycast points instead of logs. 
Each RaycastPoint has log data from the users Accelorometer, gps, gyroscope, devices current wifi signal strength, ping time on all available devices. 
We can then take this raycast data, using triangulation and clustering and see a mapped area to some degree. 



## How to build a 3D WiFi scanner:
1. Install the unity app on your phone using unitys software
2. Install unity,visual Studio, c#, python, Rabbitmq, log4net, ELk. 
3. Run the needed services. 
4. Run the app on your phone.
5. Run the python code 4D python mapping and youll eventually get a 4d mapped area! 


## What this can be used for?
As always computer science has many use cases and I ask you have ethics in mind with this project. 
That being said you can use this on drones and map alot. 
you can use this for a new form of ground penetrating radar like I have been using. 
Enjoy:) 

##License
This project is licensed for educational use only.
Any commercial use without proper authorization will result in legal action.
Feel free to make youtube videos with it! 
Enjoy exploring and mapping with the 3D WiFi Scanner!
