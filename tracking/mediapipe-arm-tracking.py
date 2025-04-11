import cv2
import mediapipe as mp
import json
import time
import asyncio
import websockets
import threading
import queue

class ArmTracker:
    def __init__(self, ws_host="localhost", ws_port=8765):
        # Initialize MediaPipe Pose
        self.mp_pose = mp.solutions.pose
        self.pose = self.mp_pose.Pose(
            static_image_mode=False,
            model_complexity=1,
            smooth_landmarks=True,
            min_detection_confidence=0.5,
            min_tracking_confidence=0.5
        )
        self.mp_drawing = mp.solutions.drawing_utils
        
        # WebSocket server settings
        self.ws_host = ws_host
        self.ws_port = ws_port
        self.connected_clients = set()
        self.server_running = False
        
        # Queue for passing data between threads
        self.message_queue = queue.Queue()
        
        # Start WebSocket server in a separate thread
        self.server_thread = threading.Thread(target=self.start_server)
        self.server_thread.daemon = True
        self.server_thread.start()
    
    def start_server(self):
        """Start WebSocket server in a separate thread"""
        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)
        
        async def handler(websocket, path):
            print(f"Client connected from {websocket.remote_address}")
            self.connected_clients.add(websocket)
            print(f"Client connected. Total clients: {len(self.connected_clients)}")
            try:
                # Send a welcome message to confirm connection
                await websocket.send(json.dumps({"status": "connected"}))
                # Keep the connection alive
                await websocket.wait_closed()
            finally:
                self.connected_clients.remove(websocket)
                print(f"Client disconnected. Total clients: {len(self.connected_clients)}")
        
        async def broadcast_messages():
            while True:
                # Check for messages every 10ms
                await asyncio.sleep(0.01)
                
                # Send any queued messages
                try:
                    while not self.message_queue.empty():
                        message = self.message_queue.get_nowait()
                        if self.connected_clients:
                            json_message = json.dumps(message)
                            tasks = [ws.send(json_message) for ws in self.connected_clients]
                            await asyncio.gather(*tasks)
                except Exception as e:
                    print(f"Error broadcasting: {e}")
        
        async def run_server():
            self.server = await websockets.serve(handler, self.ws_host, self.ws_port)
            self.server_running = True
            print(f"WebSocket server running at ws://{self.ws_host}:{self.ws_port}")
            
            # Start the message broadcaster
            asyncio.create_task(broadcast_messages())
            
            # Keep server running
            await asyncio.Future()
        
        # Run the server
        loop.run_until_complete(run_server())
    
    def queue_message(self, message):
        """Add a message to the broadcasting queue"""
        self.message_queue.put(message)
    
    def start_camera(self, camera_id=0, width=640, height=480):
        """Start webcam capture and process frames"""
        cap = cv2.VideoCapture(camera_id)
        cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
        
        if not cap.isOpened():
            print("Error: Could not open camera.")
            return
        
        print("Controls:")
        print("Q - Quit")
        print(f"WebSocket server running at ws://{self.ws_host}:{self.ws_port}")
        
        while cap.isOpened():
            success, image = cap.read()
            if not success:
                print("Ignoring empty camera frame.")
                continue
            
            # Convert to RGB for MediaPipe
            image_rgb = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
            
            # Process frame with MediaPipe
            results = self.pose.process(image_rgb)
            
            # Draw pose landmarks on the image
            if results.pose_landmarks:
                self.mp_drawing.draw_landmarks(
                    image, 
                    results.pose_landmarks,
                    self.mp_pose.POSE_CONNECTIONS
                )
                
                # Process and send only arm data
                arm_data = self.extract_arm_data(results)
                
                # Queue the data for broadcasting
                if self.server_running:
                    self.queue_message(arm_data)
            
            # Add connected clients indicator
            client_count = len(self.connected_clients)
            cv2.putText(image, f"CLIENTS: {client_count}", (20, 30), 
                        cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 0, 255), 2)
            
            # Highlight wrist positions
            if results.pose_landmarks:
                self.highlight_wrists(image, results.pose_landmarks)
            
            # Display the resulting frame
            cv2.imshow('MediaPipe Arm Tracking', image)
            
            # Process key presses
            key = cv2.waitKey(5) & 0xFF
            if key == ord('q'):
                break
        
        # Clean up
        cap.release()
        cv2.destroyAllWindows()
        self.pose.close()
    
    def highlight_wrists(self, image, landmarks):
        """Highlight wrist positions for better visibility"""
        h, w, _ = image.shape
        
        # Draw left wrist
        left_wrist = landmarks.landmark[self.mp_pose.PoseLandmark.LEFT_WRIST]
        left_x = int(left_wrist.x * w)
        left_y = int(left_wrist.y * h)
        cv2.circle(image, (left_x, left_y), 15, (0, 255, 0), -1)
        cv2.putText(image, "LEFT", (left_x - 25, left_y - 20), 
                    cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 0), 2)
        
        # Draw right wrist
        right_wrist = landmarks.landmark[self.mp_pose.PoseLandmark.RIGHT_WRIST]
        right_x = int(right_wrist.x * w)
        right_y = int(right_wrist.y * h)
        cv2.circle(image, (right_x, right_y), 15, (255, 0, 0), -1)
        cv2.putText(image, "RIGHT", (right_x - 25, right_y - 20), 
                    cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 0, 0), 2)
    
    def extract_arm_data(self, results):
        """Extract only arm position data needed for rigid arm control"""
        timestamp = time.time()
        
        # Get indices for landmarks we care about
        LEFT_WRIST = self.mp_pose.PoseLandmark.LEFT_WRIST.value
        RIGHT_WRIST = self.mp_pose.PoseLandmark.RIGHT_WRIST.value
        LEFT_SHOULDER = self.mp_pose.PoseLandmark.LEFT_SHOULDER.value
        RIGHT_SHOULDER = self.mp_pose.PoseLandmark.RIGHT_SHOULDER.value
        
        # Initialize data structures
        arm_data = {
            'timestamp': timestamp,
            'left_arm': None,
            'right_arm': None
        }
        
        # Extract data from 3D world landmarks (better for spatial positioning)
        if results.pose_world_landmarks:
            landmarks = results.pose_world_landmarks.landmark
            
            # Get left arm data (shoulder and wrist)
            if landmarks[LEFT_SHOULDER].visibility > 0.5 and landmarks[LEFT_WRIST].visibility > 0.5:
                arm_data['left_arm'] = {
                    'shoulder': {
                        'x': landmarks[LEFT_SHOULDER].x,
                        'y': landmarks[LEFT_SHOULDER].y,
                        'z': landmarks[LEFT_SHOULDER].z,
                        'visibility': landmarks[LEFT_SHOULDER].visibility
                    },
                    'wrist': {
                        'x': landmarks[LEFT_WRIST].x,
                        'y': landmarks[LEFT_WRIST].y,
                        'z': landmarks[LEFT_WRIST].z,
                        'visibility': landmarks[LEFT_WRIST].visibility
                    }
                }
            
            # Get right arm data (shoulder and wrist)
            if landmarks[RIGHT_SHOULDER].visibility > 0.5 and landmarks[RIGHT_WRIST].visibility > 0.5:
                arm_data['right_arm'] = {
                    'shoulder': {
                        'x': landmarks[RIGHT_SHOULDER].x,
                        'y': landmarks[RIGHT_SHOULDER].y,
                        'z': landmarks[RIGHT_SHOULDER].z,
                        'visibility': landmarks[RIGHT_SHOULDER].visibility
                    },
                    'wrist': {
                        'x': landmarks[RIGHT_WRIST].x,
                        'y': landmarks[RIGHT_WRIST].y,
                        'z': landmarks[RIGHT_WRIST].z,
                        'visibility': landmarks[RIGHT_WRIST].visibility
                    }
                }
        
        return arm_data

# Example usage
if __name__ == "__main__":
    tracker = ArmTracker(ws_host="127.0.0.1", ws_port=8765)
    tracker.start_camera()