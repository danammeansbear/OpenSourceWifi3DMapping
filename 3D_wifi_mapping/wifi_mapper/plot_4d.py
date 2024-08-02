import pika
import json

# Connect to RabbitMQ
connection = pika.BlockingConnection(pika.ConnectionParameters('localhost'))
channel = connection.channel()

# Declare the queue
channel.queue_declare(queue='device_logs')

# Define a callback function to process the messages
def callback(ch, method, properties, body):
    # Decode the message body from bytes to string
    body_str = body.decode('utf-8')

    # Parse the JSON string to a dictionary
    log = json.loads(body_str)

    # Extract the log values
    user_info = log.get('User')
    os = log.get('OS')
    device_model = log.get('Device Model')
    geolocation_data = log.get('Geolocation Data')
    accelerometer_data = log.get('Accelerometer Data')
    available_devices = log.get('Available Devices')
    wifi_signal_strength = log.get('Wi-Fi Signal Strength')

    # Print the log values
    print(f"User Info: {user_info}")
    print(f"OS: {os}")
    print(f"Device Model: {device_model}")
    print(f"Geolocation Data: {geolocation_data}")
    print(f"Accelerometer Data: {accelerometer_data}")
    print(f"Available Devices: {available_devices}")
    print(f"Wi-Fi Signal Strength: {wifi_signal_strength}")

# Set the callback function for the 'device_logs' queue
channel.basic_consume(queue='device_logs', on_message_callback=callback, auto_ack=True)

# Start consuming messages
channel.start_consuming()
