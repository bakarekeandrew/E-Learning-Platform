// Initialize SignalR connection
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/dashboardHub")
    .withAutomaticReconnect()
    .build();

// Charts initialization
let courseProgressChart;
let quizParticipationChart;

// Start the connection
async function startConnection() {
    try {
        await connection.start();
        console.log("SignalR Connected.");
    } catch (err) {
        console.log(err);
        setTimeout(startConnection, 5000);
    }
}

// Initialize charts
function initializeCharts() {
    // Course Progress Chart
    const courseCtx = document.getElementById('courseProgressChart').getContext('2d');
    courseProgressChart = new Chart(courseCtx, {
        type: 'line',
        data: {
            labels: [],
            datasets: [{
                label: 'Average Progress',
                data: [],
                borderColor: 'rgb(75, 192, 192)',
                tension: 0.1
            }]
        },
        options: {
            responsive: true,
            scales: {
                y: {
                    beginAtZero: true,
                    max: 100
                }
            }
        }
    });

    // Quiz Participation Chart
    const quizCtx = document.getElementById('quizParticipationChart').getContext('2d');
    quizParticipationChart = new Chart(quizCtx, {
        type: 'doughnut',
        data: {
            labels: [],
            datasets: [{
                data: [],
                backgroundColor: [
                    'rgb(255, 99, 132)',
                    'rgb(54, 162, 235)',
                    'rgb(255, 205, 86)'
                ]
            }]
        },
        options: {
            responsive: true
        }
    });
}

// Handle real-time updates
connection.on("UpdateActiveUsers", (count) => {
    document.getElementById("activeUsersCount").textContent = count;
});

connection.on("CourseProgressUpdated", (courseId, userId, progress) => {
    // Update course progress chart
    const date = new Date().toLocaleTimeString();
    courseProgressChart.data.labels.push(date);
    courseProgressChart.data.datasets[0].data.push(progress);
    
    // Keep only last 10 data points
    if (courseProgressChart.data.labels.length > 10) {
        courseProgressChart.data.labels.shift();
        courseProgressChart.data.datasets[0].data.shift();
    }
    
    courseProgressChart.update();
});

connection.on("QuizParticipationUpdated", (quizId, participantCount) => {
    // Update quiz participation chart
    const quizIndex = quizParticipationChart.data.labels.indexOf(quizId);
    
    if (quizIndex === -1) {
        quizParticipationChart.data.labels.push(quizId);
        quizParticipationChart.data.datasets[0].data.push(participantCount);
    } else {
        quizParticipationChart.data.datasets[0].data[quizIndex] = participantCount;
    }
    
    quizParticipationChart.update();
});

connection.on("ReceiveNotification", (message, type) => {
    const notificationsList = document.getElementById("notificationsList");
    const notification = document.createElement("div");
    notification.className = `list-group-item list-group-item-${type}`;
    notification.textContent = message;
    
    // Add timestamp
    const timestamp = document.createElement("small");
    timestamp.className = "float-end text-muted";
    timestamp.textContent = new Date().toLocaleTimeString();
    notification.appendChild(timestamp);
    
    // Add to top of list
    notificationsList.insertBefore(notification, notificationsList.firstChild);
    
    // Keep only last 10 notifications
    if (notificationsList.children.length > 10) {
        notificationsList.removeChild(notificationsList.lastChild);
    }
});

// Initialize everything when the page loads
document.addEventListener('DOMContentLoaded', () => {
    initializeCharts();
    startConnection();
}); 